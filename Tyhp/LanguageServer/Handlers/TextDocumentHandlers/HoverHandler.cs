namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Checker;

    /// <summary>
    /// LSP <c>textDocument/hover</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Type information and documentation for the symbol under the cursor.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentHoverName, UseSingleObjectParameterDeserialization = true)]
        public async Task<Hover?> HandleHover(
            TextDocumentPositionParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                Hover? structKey = await this.TryStructKeyHoverAsync(arg, cancellationToken).ConfigureAwait(false);
                if (structKey is not null)
                {
                    return structKey;
                }

                (Workspace.DocumentState State, SymbolLookupResult Lookup)? resolved =
                    await this.LookupSymbolAsync(arg, cancellationToken).ConfigureAwait(false);
                if (resolved is null)
                {
                    return null;
                }

                SymbolLookupResult lookup = resolved.Value.Lookup;
                ICheckedType? inferred = this._analysis.GetInferredType(lookup.InferredTypeNode);
                if (inferred is null && !ReferenceEquals(lookup.InferredTypeNode, lookup.Node))
                {
                    inferred = this._analysis.GetInferredType(lookup.Node);
                }
                if (inferred is { Kind: CheckedTypeKind.Unresolved })
                {
                    // The checker uses this sentinel when it genuinely could not infer a type
                    // (e.g. an error elsewhere). Showing the literal word "unresolved" as a type
                    // is confusing, so treat it the same as no inferred type at all.
                    inferred = null;
                }

                string? markdown = SymbolFormatter.TryFormatTypeExpressionHover(
                    lookup.Node,
                    lookup.Symbol);
                if (string.IsNullOrEmpty(markdown) && lookup.Symbol is BaseSymbol symbol)
                {
                    markdown = SymbolFormatter.FormatHover(symbol, inferred);
                }
                else if (string.IsNullOrEmpty(markdown) && inferred is not null)
                {
                    markdown = SymbolFormatter.FormatInferredHover(GetHoverName(lookup.Node), inferred);
                }

                if (string.IsNullOrEmpty(markdown))
                {
                    return null;
                }

                return new Hover
                {
                    Contents = new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = markdown,
                    },
                    Range = PositionUtilities.ToLspRange(lookup.Node),
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/hover", ex);
                return null;
            }
        }

        private async Task<Hover?> TryStructKeyHoverAsync(
            TextDocumentPositionParams arg,
            CancellationToken cancellationToken)
        {
            if (arg.TextDocument?.Uri is null || arg.Position is null)
            {
                return null;
            }

            (Workspace.DocumentState State, Tyhp.TyhpLang.Ast.SrcFileAst Ast, string Content)? document =
                await this.GetAnalyzedAstAsync(arg.TextDocument.Uri, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return null;
            }

            var (line, column) = PositionUtilities.FromLspPosition(arg.Position);
            IReadOnlyList<Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast>? path = this._symbolFinder.FindPathAtPosition(
                document.Value.Ast,
                line,
                column);
            if (path is null)
            {
                return null;
            }

            Tyhp.TyhpLang.Binder.Scopes.GlobalScope? scope = this._analysis.GetGlobalScope();
            Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope? fromScope = scope is null
                ? null
                : this._symbolFinder.FindScopeAtPosition(document.Value.Ast, scope, line, column);
            if (!StructInitializerSupport.TryGetHoverField(
                path,
                scope,
                this._analysis.GetSymbolTree(),
                fromScope ?? scope,
                this._analysis.GetInferredType,
                out Tyhp.TyhpLang.Binder.Symbols.ObjectPropertySymbol? property,
                out Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast? keyNode)
                || property is null
                || keyNode is null)
            {
                return null;
            }

            return new Hover
            {
                Contents = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = SymbolFormatter.FormatStructFieldHover(property),
                },
                Range = PositionUtilities.ToLspRange(keyNode),
            };
        }

        private static string? GetHoverName(Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast node)
        {
            string name = SymbolFinder.GetDisplayName(node);
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }
}
