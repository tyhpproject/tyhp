namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;

    /// <summary>
    /// LSP <c>textDocument/selectionRange</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        internal const string TextDocumentSelectionRangeName = "textDocument/selectionRange";

        /// <summary>
        /// Smart selection expansion from token → expression → statement → block →
        /// function → class → file.
        /// </summary>
        [JsonRpcMethod(TextDocumentSelectionRangeName, UseSingleObjectParameterDeserialization = true)]
        public async Task<SelectionRange[]> HandleSelectionRange(
            SelectionRangeParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null || arg.Positions is null || arg.Positions.Length == 0)
                {
                    return [];
                }

                (DocumentState State, SrcFileAst Ast, string Content)? document =
                    await this.GetAnalyzedAstAsync(arg.TextDocument.Uri, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    return [];
                }

                return SelectionRangeCollector.Collect(
                    document.Value.Ast,
                    document.Value.Content,
                    arg.Positions,
                    this._symbolFinder);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure(TextDocumentSelectionRangeName, ex);
                return [];
            }
        }
    }
}
