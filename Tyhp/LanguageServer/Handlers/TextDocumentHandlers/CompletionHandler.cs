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
    /// LSP <c>textDocument/completion</c> and <c>completionItem/resolve</c> handlers
    /// on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Context-aware autocomplete for the cursor position.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentCompletionName, UseSingleObjectParameterDeserialization = true)]
        public async Task<CompletionList> HandleCompletion(
            CompletionParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg?.TextDocument?.Uri is null || arg.Position is null)
                {
                    return EmptyCompletionList();
                }

                Uri uri = arg.TextDocument.Uri;
                DocumentState? state = await this.EnsureAnalyzedAsync(uri, cancellationToken).ConfigureAwait(false);
                if (state is null || !AnalysisService.IsAnalyzableLanguage(state.LanguageMode))
                {
                    return EmptyCompletionList();
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
                return CompletionEngine.Complete(
                    content,
                    arg.Position,
                    arg.Context,
                    ast,
                    scope,
                    tree,
                    this._symbolFinder,
                    this._analysis.GetInferredType);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/completion", ex);
                return EmptyCompletionList();
            }
        }

        /// <summary>
        /// Fills deferred documentation on a previously returned completion item.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentCompletionResolveName, UseSingleObjectParameterDeserialization = true)]
        public CompletionItem HandleCompletionResolve(CompletionItem arg, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arg is null)
                {
                    return new CompletionItem { Label = string.Empty };
                }

                CompletionEngine.Resolve(arg);
                return arg;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("completionItem/resolve", ex);
                return arg ?? new CompletionItem { Label = string.Empty };
            }
        }

        private static CompletionList EmptyCompletionList()
        {
            return new CompletionList
            {
                IsIncomplete = false,
                Items = [],
            };
        }
    }
}
