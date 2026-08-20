namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Scopes;

    /// <summary>
    /// LSP <c>textDocument/signatureHelp</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Parameter hints for the call whose argument list contains the cursor.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentSignatureHelpName, UseSingleObjectParameterDeserialization = true)]
        public async Task<SignatureHelp?> HandleSignatureHelp(
            SignatureHelpParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null || arg.Position is null)
                {
                    return null;
                }

                DocumentState? state = await this.EnsureAnalyzedAsync(arg.TextDocument.Uri, cancellationToken)
                    .ConfigureAwait(false);
                if (state is null || !AnalysisService.IsAnalyzableLanguage(state.LanguageMode))
                {
                    return null;
                }

                string content;
                SrcFileAst? ast;
                lock (state.SyncRoot)
                {
                    content = state.Content;
                    ast = state.ParsedAst;
                }

                GlobalScope? scope = this._analysis.GetGlobalScope();
                SymbolTree? tree = this._analysis.GetSymbolTree();
                return SignatureHelpEngine.Provide(
                    content,
                    arg.Position,
                    ast,
                    scope,
                    tree,
                    this._symbolFinder);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/signatureHelp", ex);
                return null;
            }
        }
    }
}
