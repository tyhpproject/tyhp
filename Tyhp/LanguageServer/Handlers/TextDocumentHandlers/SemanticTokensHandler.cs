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
    /// LSP <c>textDocument/semanticTokens/full</c> and <c>full/delta</c> handlers
    /// on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        private int _semanticTokensGeneration;

        /// <summary>
        /// Full-document semantic tokens encoded as relative 5-tuples.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentSemanticTokensFullName, UseSingleObjectParameterDeserialization = true)]
        public async Task<SemanticTokens> HandleSemanticTokensFull(
            SemanticTokensParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null)
                {
                    return EmptyTokens();
                }

                return await this.BuildSemanticTokensAsync(arg.TextDocument.Uri, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure(Methods.TextDocumentSemanticTokensFullName, ex);
                return EmptyTokens();
            }
        }

        /// <summary>
        /// Incremental semantic-token update relative to <see cref="SemanticTokensDeltaParams.PreviousResultId"/>.
        /// Returns <see cref="SemanticTokensDelta"/> when the previous result is known, or a full
        /// <see cref="SemanticTokens"/> payload when it is not (per the LSP union result type).
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentSemanticTokensFullDeltaName, UseSingleObjectParameterDeserialization = true)]
        public async Task<object> HandleSemanticTokensFullDelta(
            SemanticTokensDeltaParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null)
                {
                    return EmptyTokens();
                }

                Uri uri = arg.TextDocument.Uri;
                DocumentState? state = this._workspace.GetDocument(uri);
                string? previousId;
                int[] previousData;
                if (state is null)
                {
                    previousId = null;
                    previousData = [];
                }
                else
                {
                    lock (state.SyncRoot)
                    {
                        previousId = state.SemanticTokensResultId;
                        previousData = state.SemanticTokensData;
                    }
                }

                SemanticTokens full = await this.BuildSemanticTokensAsync(uri, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(arg.PreviousResultId)
                    || !string.Equals(arg.PreviousResultId, previousId, StringComparison.Ordinal))
                {
                    return full;
                }

                return new SemanticTokensDelta
                {
                    ResultId = full.ResultId,
                    Edits = SemanticTokenCollector.ComputeDelta(previousData, full.Data ?? []),
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure(Methods.TextDocumentSemanticTokensFullDeltaName, ex);
                return EmptyTokens();
            }
        }

        private async Task<SemanticTokens> BuildSemanticTokensAsync(Uri uri, CancellationToken cancellationToken)
        {
            (DocumentState State, SrcFileAst Ast, string Content)? document =
                await this.GetAnalyzedAstAsync(uri, cancellationToken).ConfigureAwait(false);
            int[] data;
            DocumentState? state;
            if (document is null)
            {
                data = [];
                state = this._workspace.GetDocument(uri);
            }
            else
            {
                state = document.Value.State;
                GlobalScope? scope = this._analysis.GetGlobalScope();
                SymbolTree? tree = this._analysis.GetSymbolTree();
                data = SemanticTokenCollector.CollectData(
                    document.Value.Ast,
                    document.Value.Content,
                    scope,
                    tree,
                    this._symbolFinder);
            }

            string resultId = Interlocked.Increment(ref this._semanticTokensGeneration).ToString();
            if (state is not null)
            {
                lock (state.SyncRoot)
                {
                    state.SemanticTokensResultId = resultId;
                    state.SemanticTokensData = data;
                }
            }

            return new SemanticTokens
            {
                ResultId = resultId,
                Data = data,
            };
        }

        private static SemanticTokens EmptyTokens()
            => new() { ResultId = "0", Data = [] };
    }
}
