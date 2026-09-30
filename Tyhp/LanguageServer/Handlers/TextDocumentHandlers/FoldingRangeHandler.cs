namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;

    /// <summary>
    /// LSP <c>textDocument/foldingRange</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Foldable regions for classes, functions, blocks, comments, and import groups.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentFoldingRangeName, UseSingleObjectParameterDeserialization = true)]
        public async Task<FoldingRange[]> HandleFoldingRange(
            FoldingRangeParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null)
                {
                    return [];
                }

                DocumentState? state = await this.EnsureAnalyzedAsync(arg.TextDocument.Uri, cancellationToken)
                    .ConfigureAwait(false);
                if (state is null || !AnalysisService.IsAnalyzableLanguage(state.LanguageMode))
                {
                    return [];
                }

                string content;
                SrcFileAst? ast;
                lock (state.SyncRoot)
                {
                    content = state.Content;
                    ast = state.ParsedAst;
                }

                return FoldingRangeCollector.Collect(ast, content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/foldingRange", ex);
                return [];
            }
        }
    }
}
