namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.CLI;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;
    using Tyhp.LanguageServer.Handlers;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Builds LSP code actions (quick fixes and source organize-imports) from
    /// document diagnostics and binder symbols.
    /// </summary>
    internal static class CodeActionEngine
    {
        private static readonly HashSet<MessageCode> UnresolvedNameCodes =
        [
            MessageCode.BinderSymbolNotFound,
            MessageCode.BinderUnresolvedExtendsType,
            MessageCode.BinderUnresolvedImplementsType,
            MessageCode.BinderUnresolvedReturnType,
            MessageCode.BinderUnresolvedParameterType,
            MessageCode.BinderUnresolvedGenericConstraintType,
            MessageCode.BinderUnresolvedGenericDefaultType,
            MessageCode.ExtensionOperatorTargetNotFound,
        ];

        /// <summary>
        /// Code actions applicable to <paramref name="range"/> in <paramref name="state"/>.
        /// </summary>
        public static CodeAction[] Collect(
            DocumentState state,
            SrcFileAst ast,
            ProtocolRange range,
            CodeActionContext? context,
            GlobalScope? globalScope,
            IBaseScope? fromScope)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(ast);
            range ??= new ProtocolRange
            {
                Start = new Position { Line = 0, Character = 0 },
                End = new Position { Line = 0, Character = 0 },
            };

            string content;
            IReadOnlyList<IDiagnostic> diagnostics;
            lock (state.SyncRoot)
            {
                content = state.Content;
                diagnostics = state.Diagnostics;
            }

            CodeActionKind[]? only = context?.Only;
            var actions = new List<CodeAction>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (WantsKind(CodeActionKind.QuickFix, only))
            {
                foreach (IDiagnostic diagnostic in diagnostics)
                {
                    if (!Overlaps(range, PositionUtilities.ToLspRange(diagnostic)))
                    {
                        continue;
                    }

                    if (diagnostic.Code == MessageCode.CheckerUnusedImport)
                    {
                        CodeAction? remove = TryRemoveUnusedImport(state.Uri, content, ast, diagnostic);
                        if (remove is not null && seen.Add(ActionKey(remove)))
                        {
                            actions.Add(remove);
                        }

                        continue;
                    }

                    if (!UnresolvedNameCodes.Contains(diagnostic.Code))
                    {
                        continue;
                    }

                    foreach (CodeAction import in CreateAutoImportActions(
                        state.Uri,
                        content,
                        diagnostic,
                        globalScope,
                        fromScope))
                    {
                        if (seen.Add(ActionKey(import)))
                        {
                            actions.Add(import);
                        }
                    }
                }
            }

            if (WantsKind(CodeActionKind.SourceOrganizeImports, only)
                || WantsKind(CodeActionKind.Source, only))
            {
                CodeAction? organize = TryOrganizeImports(state.Uri, content);
                if (organize is not null && seen.Add(ActionKey(organize)))
                {
                    actions.Add(organize);
                }
            }

            return [.. actions];
        }

        private static IEnumerable<CodeAction> CreateAutoImportActions(
            Uri uri,
            string content,
            IDiagnostic diagnostic,
            GlobalScope? globalScope,
            IBaseScope? fromScope)
        {
            string unresolved = FirstFormatString(diagnostic);
            if (string.IsNullOrEmpty(unresolved))
            {
                yield break;
            }

            bool typesOnly = diagnostic.Code != MessageCode.BinderSymbolNotFound;
            LspDiagnostic lspDiagnostic = DiagnosticsPublisher.ToLspDiagnostic(diagnostic);
            var seenSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in SplitUnresolvedTypeNames(unresolved))
            {
                IReadOnlyList<BaseSymbol> matches = UseStatementEdits.FindImportableMatches(
                    globalScope,
                    fromScope,
                    candidate,
                    typesOnly);
                foreach (BaseSymbol symbol in matches)
                {
                    if (!seenSymbols.Add(symbol.FullyQualifiedName))
                    {
                        continue;
                    }

                    TextEdit[]? edits = UseStatementEdits.TryCreateImportEdits(fromScope, symbol, content);
                    if (edits is null || edits.Length == 0)
                    {
                        continue;
                    }

                    string fqn = symbol.FullyQualifiedName.TrimStart('\\');
                    yield return new CodeAction
                    {
                        Title = Message.Localize("CLI_LspCodeActionImport", fqn),
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = [lspDiagnostic],
                        Edit = WorkspaceEditFor(uri, edits),
                    };
                }
            }
        }

        /// <summary>
        /// Splits a binder-formatted unresolved type name (e.g. <c>?User</c>,
        /// <c>Foo|Bar</c>, <c>Foo&amp;Bar</c>) into the individual simple/qualified
        /// names it references. The binder's type display formatter (source of these
        /// diagnostic format args) prefixes nullable types with <c>?</c> and joins
        /// unions/intersections with <c>|</c>/<c>&amp;</c> — without this split,
        /// <see cref="UseStatementEdits.SimpleName"/> would treat the whole decorated
        /// string as one identifier and never match a real symbol name.
        /// </summary>
        private static IEnumerable<string> SplitUnresolvedTypeNames(string typeName)
        {
            string trimmed = typeName.Trim().TrimStart('?');
            if (trimmed.IndexOfAny(['|', '&']) < 0)
            {
                if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }

                yield break;
            }

            foreach (string part in trimmed.Split(['|', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrEmpty(part))
                {
                    yield return part;
                }
            }
        }

        private static CodeAction? TryRemoveUnusedImport(
            Uri uri,
            string content,
            SrcFileAst ast,
            IDiagnostic diagnostic)
        {
            string imported = FirstFormatString(diagnostic);
            TextEdit? edit = UseStatementEdits.TryCreateRemoveImportEdit(
                content,
                ast,
                imported,
                diagnostic.Line);
            if (edit is null)
            {
                return null;
            }

            string titleName = string.IsNullOrEmpty(imported) ? diagnostic.Message : imported;
            return new CodeAction
            {
                Title = Message.Localize("CLI_LspCodeActionRemoveUnusedImport", titleName),
                Kind = CodeActionKind.QuickFix,
                Diagnostics = [DiagnosticsPublisher.ToLspDiagnostic(diagnostic)],
                Edit = WorkspaceEditFor(uri, [edit]),
            };
        }

        private static CodeAction? TryOrganizeImports(Uri uri, string content)
        {
            string sorted = UseStatementEdits.SortImports(content);
            if (string.Equals(sorted, content, StringComparison.Ordinal))
            {
                return null;
            }

            TextEdit[] edits = DocumentFormatter.DiffEdits(content, sorted);
            if (edits.Length == 0)
            {
                return null;
            }

            return new CodeAction
            {
                Title = Message.Localize("CLI_LspCodeActionOrganizeImports"),
                Kind = CodeActionKind.SourceOrganizeImports,
                Edit = WorkspaceEditFor(uri, edits),
            };
        }

        private static WorkspaceEdit WorkspaceEditFor(Uri uri, TextEdit[] edits)
        {
            string key = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
            return new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>(StringComparer.Ordinal)
                {
                    [key] = edits,
                },
            };
        }

        private static string FirstFormatString(IDiagnostic diagnostic)
        {
            if (diagnostic.FormatParams is { Length: > 0 } && diagnostic.FormatParams[0] is not null)
            {
                return diagnostic.FormatParams[0].ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool WantsKind(CodeActionKind kind, CodeActionKind[]? only)
        {
            if (only is null || only.Length == 0)
            {
                return true;
            }

            foreach (CodeActionKind requested in only)
            {
                if (requested == kind)
                {
                    return true;
                }

                if (requested == CodeActionKind.Source
                    && kind == CodeActionKind.SourceOrganizeImports)
                {
                    return true;
                }

                if (requested == CodeActionKind.QuickFix && kind == CodeActionKind.QuickFix)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Overlaps(ProtocolRange a, ProtocolRange b)
        {
            if (a.Start is null || a.End is null || b.Start is null || b.End is null)
            {
                return false;
            }

            if (Compare(a.Start, b.End) <= 0 && Compare(b.Start, a.End) <= 0)
            {
                return true;
            }

            // Binder diagnostics are often zero-width; treat a shared line as in range
            // so the lightbulb still appears when the request covers the identifier.
            int aStart = a.Start.Line;
            int aEnd = a.End.Line;
            int bStart = b.Start.Line;
            int bEnd = b.End.Line;
            return aStart <= bEnd && bStart <= aEnd;
        }

        private static int Compare(Position left, Position right)
        {
            int line = left.Line.CompareTo(right.Line);
            return line != 0 ? line : left.Character.CompareTo(right.Character);
        }

        private static string ActionKey(CodeAction action)
            => (action.Kind + "|" + action.Title) ?? string.Empty;
    }
}
