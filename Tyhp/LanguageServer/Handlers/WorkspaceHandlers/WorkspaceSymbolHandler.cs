namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;

    /// <summary>
    /// LSP <c>workspace/symbol</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Project-wide declaration search (classes, functions, constants, type aliases).
        /// </summary>
        [JsonRpcMethod(Methods.WorkspaceSymbolName, UseSingleObjectParameterDeserialization = true)]
        public async Task<SymbolInformation[]> HandleWorkspaceSymbol(
            WorkspaceSymbolParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await this._analysis.AnalyzeAllAsync(cancellationToken).ConfigureAwait(false);
                return WorkspaceSymbolSearch.Search(
                    arg?.Query,
                    this._analysis.GetGlobalScope(),
                    this._workspace);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure(Methods.WorkspaceSymbolName, ex);
                return [];
            }
        }
    }
}
