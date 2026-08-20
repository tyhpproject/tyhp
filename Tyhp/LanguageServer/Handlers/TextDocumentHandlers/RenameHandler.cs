namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// LSP <c>textDocument/prepareRename</c> and <c>textDocument/rename</c> handlers
    /// on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        private const string PrepareRenameMethodName = "textDocument/prepareRename";

        /// <summary>
        /// Returns the identifier range to rename, or null when the symbol cannot be renamed.
        /// </summary>
        [JsonRpcMethod(PrepareRenameMethodName, UseSingleObjectParameterDeserialization = true)]
        public async Task<ProtocolRange?> HandlePrepareRename(
            TextDocumentPositionParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                (DocumentState State, SymbolLookupResult Lookup)? resolved =
                    await this.LookupSymbolAsync(arg, cancellationToken).ConfigureAwait(false);
                if (resolved is null || !IsRenameable(resolved.Value.Lookup))
                {
                    return null;
                }

                string currentName = CurrentSymbolName(resolved.Value.Lookup);
                string? content;
                lock (resolved.Value.State.SyncRoot)
                {
                    content = resolved.Value.State.Content;
                }

                IBase2Ast nameNode = SymbolFinder.PreferIdentifierNode(resolved.Value.Lookup.Node);
                return PositionUtilities.ToIdentifierRange(nameNode, currentName, content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure(PrepareRenameMethodName, ex);
                return null;
            }
        }

        /// <summary>
        /// Renames a symbol at every reference, including the declaration.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentRenameName, UseSingleObjectParameterDeserialization = true)]
        public async Task<WorkspaceEdit?> HandleRename(
            RenameParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                if (arg?.TextDocument?.Uri is null || arg.Position is null)
                {
                    return null;
                }

                var position = new TextDocumentPositionParams
                {
                    TextDocument = arg.TextDocument,
                    Position = arg.Position,
                };
                (DocumentState State, SymbolLookupResult Lookup)? resolved =
                    await this.LookupSymbolAsync(position, cancellationToken).ConfigureAwait(false);
                if (resolved is null || !IsRenameable(resolved.Value.Lookup))
                {
                    return null;
                }

                string newName = arg.NewName ?? string.Empty;
                if (!IdentifierSyntax.IsValidIdentifier(newName))
                {
                    return null;
                }

                if (HasRenameConflict(resolved.Value.Lookup, newName, resolved.Value.State))
                {
                    return null;
                }

                string currentName = CurrentSymbolName(resolved.Value.Lookup);
                IReadOnlyList<SymbolReference> occurrences = this.FindProjectReferences(
                    resolved.Value.Lookup,
                    cancellationToken);
                if (occurrences.Count == 0)
                {
                    return null;
                }

                var grouped = new Dictionary<string, List<TextEdit>>(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SymbolReference occurrence in occurrences)
                {
                    if (IsKeywordOccurrence(occurrence.Node))
                    {
                        continue;
                    }

                    Location? location = this.ToReferenceLocation(
                        occurrence,
                        resolved.Value.State.Uri,
                        currentName);
                    if (location?.Uri is null || location.Range is null)
                    {
                        continue;
                    }

                    string uriKey = location.Uri.IsAbsoluteUri
                        ? location.Uri.AbsoluteUri
                        : location.Uri.ToString();
                    string editKey = $"{uriKey}:{location.Range.Start?.Line}:{location.Range.Start?.Character}:{location.Range.End?.Line}:{location.Range.End?.Character}";
                    if (!seen.Add(editKey))
                    {
                        continue;
                    }

                    string original = this.ReadRangeText(location.Uri, FirstNonEmpty(occurrence.File.Identifier, occurrence.File.FileName), location.Range)
                        ?? currentName;
                    string replacement = ReplacementText(original, newName);
                    if (!grouped.TryGetValue(uriKey, out List<TextEdit>? edits))
                    {
                        edits = [];
                        grouped[uriKey] = edits;
                    }

                    edits.Add(new TextEdit
                    {
                        Range = location.Range,
                        NewText = replacement,
                    });
                }

                if (grouped.Count == 0)
                {
                    return null;
                }

                return new WorkspaceEdit
                {
                    Changes = grouped.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToArray(),
                        StringComparer.Ordinal),
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/rename", ex);
                return null;
            }
        }

        private static bool IsRenameable(SymbolLookupResult lookup)
        {
            if (IsThisVariableNode(lookup.Node) || IsKeywordOccurrence(lookup.Node))
            {
                return false;
            }

            if (lookup.Symbol is null)
            {
                // Untyped locals are still renameable by name within the enclosing callable.
                string localName = CurrentSymbolName(lookup);
                return !string.IsNullOrEmpty(localName)
                    && !IdentifierSyntax.IsThisName(localName)
                    && !IdentifierSyntax.IsKeyword(localName);
            }

            BaseSymbol symbol = lookup.Symbol;
            if (symbol is BuiltInTypeSymbol
                or BuiltInFunctionSymbol
                or BuiltInUtilityTypeSymbol
                or MagicConstantSymbol
                or SuperGlobalSymbol)
            {
                return false;
            }

            if (IsTyhpdefSymbol(symbol))
            {
                return false;
            }

            if (string.IsNullOrEmpty(symbol.SourceFile) && symbol.DeclaringAstNode is null)
            {
                return false;
            }

            return true;
        }

        private static bool IsTyhpdefSymbol(BaseSymbol symbol)
        {
            if (string.IsNullOrEmpty(symbol.SourceFile))
            {
                return false;
            }

            return string.Equals(
                WorkspaceManager.DetectLanguageMode(symbol.SourceFile),
                WorkspaceManager.LanguageModeTyhpdef,
                StringComparison.Ordinal);
        }

        private static bool IsThisVariableNode(IBase2Ast node)
            => IdentifierSyntax.IsThisName(SymbolFinder.GetDisplayName(node));

        private static bool IsKeywordOccurrence(IBase2Ast node)
        {
            string bare = IdentifierSyntax.StripDollar(SymbolFinder.GetDisplayName(node));
            return IdentifierSyntax.IsSelfStaticParent(bare) || IdentifierSyntax.IsThisName(bare);
        }

        private bool HasRenameConflict(SymbolLookupResult lookup, string newName, DocumentState state)
        {
            string bareNew = IdentifierSyntax.StripDollar(newName);
            BaseSymbol? symbol = lookup.Symbol;
            if (symbol is null)
            {
                SrcFileAst? ast;
                lock (state.SyncRoot)
                {
                    ast = state.ParsedAst;
                }

                return ast is not null
                    && SymbolFinder.EnclosingCallableHasVariableName(
                        ast,
                        lookup.DeclaringNode ?? lookup.Node,
                        bareNew,
                        lookup.DeclaringNode ?? lookup.Node);
            }

            IBaseScope? scope = symbol.ContainingScope;
            if (scope is null)
            {
                return false;
            }

            foreach (string candidate in CandidateConflictNames(symbol, bareNew))
            {
                if (scope.FindChildSymbolByName(candidate) is BaseSymbol existing
                    && !IsSameSymbol(existing, symbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> CandidateConflictNames(BaseSymbol symbol, string bareNew)
        {
            yield return bareNew;
            if (symbol is VariableSymbol or ObjectPropertySymbol or SuperGlobalSymbol)
            {
                yield return IdentifierSyntax.EnsureDollar(bareNew);
            }
        }

        private static bool IsSameSymbol(BaseSymbol left, BaseSymbol right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return left.SymbolType == right.SymbolType
                && string.Equals(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal)
                && string.Equals(left.SourceFile, right.SourceFile, StringComparison.Ordinal)
                && left.Line == right.Line
                && left.Column == right.Column;
        }

        private static string ReplacementText(string original, string newName)
        {
            string bareNew = IdentifierSyntax.StripDollar(newName);
            if (original.StartsWith('$'))
            {
                return "$" + bareNew;
            }

            return bareNew;
        }

        private string? ReadRangeText(Uri uri, string? sourceFile, ProtocolRange range)
        {
            string? content = this.GetDocumentContent(uri, sourceFile);
            if (string.IsNullOrEmpty(content) || range.Start is null || range.End is null)
            {
                return null;
            }

            int start = PositionUtilities.GetOffset(content, range.Start);
            int end = PositionUtilities.GetOffset(content, range.End);
            if (end < start || start < 0 || end > content.Length)
            {
                return null;
            }

            return content[start..end];
        }
    }
}
