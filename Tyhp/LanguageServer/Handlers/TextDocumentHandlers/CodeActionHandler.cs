namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;

    /// <summary>
    /// LSP <c>textDocument/codeAction</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Quick fixes (auto-import, remove unused import) and organize-imports.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentCodeActionName, UseSingleObjectParameterDeserialization = true)]
        public async Task<CodeAction[]> HandleCodeAction(
            CodeActionParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null || arg.Range is null)
                {
                    return [];
                }

                (DocumentState State, SrcFileAst Ast, string Content)? document =
                    await this.GetAnalyzedAstAsync(arg.TextDocument.Uri, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    return [];
                }

                GlobalScope? scope = this._analysis.GetGlobalScope();
                var (line, column) = PositionUtilities.FromLspPosition(arg.Range.Start ?? new Position());
                IBaseScope? fromScope = this._symbolFinder.FindScopeAtPosition(
                    document.Value.Ast,
                    scope,
                    line,
                    column) ?? scope;
                return CodeActionEngine.Collect(
                    document.Value.State,
                    document.Value.Ast,
                    arg.Range,
                    arg.Context,
                    scope,
                    fromScope);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/codeAction", ex);
                return [];
            }
        }
    }
}
