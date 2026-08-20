namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;

    /// <summary>
    /// LSP <c>textDocument/documentSymbol</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Hierarchical outline of declarations in the requested document.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDocumentSymbolName, UseSingleObjectParameterDeserialization = true)]
        public async Task<DocumentSymbol[]> HandleDocumentSymbol(
            DocumentSymbolParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null)
                {
                    return [];
                }

                (DocumentState State, SrcFileAst Ast, string Content)? document =
                    await this.GetAnalyzedAstAsync(arg.TextDocument.Uri, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    return [];
                }

                return DocumentSymbolCollector.Collect(document.Value.Ast, document.Value.Content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/documentSymbol", ex);
                return [];
            }
        }
    }
}
