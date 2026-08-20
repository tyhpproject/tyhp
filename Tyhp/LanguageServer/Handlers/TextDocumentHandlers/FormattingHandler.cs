namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// LSP <c>textDocument/formatting</c> and <c>textDocument/rangeFormatting</c>
    /// handlers on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Formats the whole document (import sort + indent normalization).
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentFormattingName, UseSingleObjectParameterDeserialization = true)]
        public async Task<TextEdit[]> HandleFormatting(
            DocumentFormattingParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null)
                {
                    return [];
                }

                return await this.FormatDocumentAsync(
                    arg.TextDocument.Uri,
                    arg.Options,
                    range: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/formatting", ex);
                return [];
            }
        }

        /// <summary>
        /// Formats a selected region only.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentRangeFormattingName, UseSingleObjectParameterDeserialization = true)]
        public async Task<TextEdit[]> HandleRangeFormatting(
            DocumentRangeFormattingParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null || arg.Range is null)
                {
                    return [];
                }

                return await this.FormatDocumentAsync(
                    arg.TextDocument.Uri,
                    arg.Options,
                    arg.Range,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/rangeFormatting", ex);
                return [];
            }
        }

        private async Task<TextEdit[]> FormatDocumentAsync(
            Uri uri,
            FormattingOptions? options,
            ProtocolRange? range,
            CancellationToken cancellationToken)
        {
            DocumentState? state = await this.EnsureAnalyzedAsync(uri, cancellationToken).ConfigureAwait(false);
            if (state is null || !AnalysisService.IsAnalyzableLanguage(state.LanguageMode))
            {
                return [];
            }

            string content;
            lock (state.SyncRoot)
            {
                content = state.Content;
            }

            return DocumentFormatter.Format(content, options, range);
        }
    }
}
