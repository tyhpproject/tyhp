namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.CLI;
    using Tyhp.LanguageServer.Workspace;

    /// <summary>
    /// Document synchronization handlers (<c>textDocument/didOpen</c>, <c>didChange</c>,
    /// <c>didClose</c>, <c>didSave</c>) on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    /// <remarks>
    /// LSP positions are 0-based lines with UTF-16 character offsets. ANTLR and the Tyhp AST
    /// use 1-based lines and 0-based character columns. Incremental content edits here stay in
    /// LSP UTF-16 space; analysis and feature handlers convert via
    /// <see cref="Analysis.PositionUtilities"/>.
    /// </remarks>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// LSP <c>textDocument/didOpen</c> — begin tracking the document and mark it dirty.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDidOpenName, UseSingleObjectParameterDeserialization = true)]
        public void HandleDidOpen(DidOpenTextDocumentParams @params)
        {
            try
            {
                if (@params?.TextDocument?.Uri is null)
                {
                    return;
                }

                DocumentState state = this._workspace.OpenDocument(
                    @params.TextDocument.Uri,
                    @params.TextDocument.Text ?? string.Empty,
                    @params.TextDocument.Version);
                this.RequestDocumentAnalysis(state.Uri, debounce: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                this.LogMessage(
                    MessageType.Log,
                    Message.Localize("CLI_LanguageServerDocumentSyncFailed", ex.GetType().Name, ex.Message));
            }
        }

        /// <summary>
        /// LSP <c>textDocument/didChange</c> — apply incremental (or full) text changes.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDidChangeName, UseSingleObjectParameterDeserialization = true)]
        public void HandleDidChange(DidChangeTextDocumentParams @params)
        {
            try
            {
                if (@params?.TextDocument?.Uri is null)
                {
                    return;
                }

                TextDocumentContentChangeEvent[] changes = @params.ContentChanges ?? [];
                DocumentState? state = this._workspace.UpdateDocument(
                    @params.TextDocument.Uri,
                    changes,
                    @params.TextDocument.Version);
                if (state is not null)
                {
                    this.RequestDocumentAnalysis(state.Uri, debounce: true);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                this.LogMessage(
                    MessageType.Log,
                    Message.Localize("CLI_LanguageServerDocumentSyncFailed", ex.GetType().Name, ex.Message));
            }
        }

        /// <summary>
        /// LSP <c>textDocument/didClose</c> — drop the document from the workspace.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDidCloseName, UseSingleObjectParameterDeserialization = true)]
        public void HandleDidClose(DidCloseTextDocumentParams @params)
        {
            try
            {
                if (@params?.TextDocument?.Uri is null)
                {
                    return;
                }

                Uri uri = @params.TextDocument.Uri;
                string filePath = this._workspace.GetDocument(uri)?.FilePath ?? WorkspaceManager.ResolveFilePath(uri);
                this._incrementalAnalyzer.Cancel(uri);
                this._workspace.CloseDocument(uri);
                this.ClearPublishedDiagnostics(uri);
                this._analysis.NotifyDocumentClosed(uri, filePath);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                this.LogMessage(
                    MessageType.Log,
                    Message.Localize("CLI_LanguageServerDocumentSyncFailed", ex.GetType().Name, ex.Message));
            }
        }

        /// <summary>
        /// LSP <c>textDocument/didSave</c> — optionally replace content when the client
        /// included text, then request re-analysis of this file only. Full project
        /// re-analysis is reserved for workspace configuration changes.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDidSaveName, UseSingleObjectParameterDeserialization = true)]
        public void HandleDidSave(DidSaveTextDocumentParams @params)
        {
            try
            {
                if (@params?.TextDocument?.Uri is null)
                {
                    return;
                }

                Uri uri = @params.TextDocument.Uri;
                DocumentState? state = this._workspace.GetDocument(uri);
                if (state is null)
                {
                    return;
                }

                if (@params.Text is not null)
                {
                    this._workspace.UpdateDocument(
                        uri,
                        [new TextDocumentContentChangeEvent { Text = @params.Text }],
                        state.Version);
                }

                this.RequestDocumentAnalysis(uri, debounce: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                this.LogMessage(
                    MessageType.Log,
                    Message.Localize("CLI_LanguageServerDocumentSyncFailed", ex.GetType().Name, ex.Message));
            }
        }

        /// <summary>
        /// Queues analysis for <paramref name="uri"/>. Debounced for change notifications;
        /// immediate for open/save.
        /// </summary>
        private void RequestDocumentAnalysis(Uri uri, bool debounce)
        {
            this._incrementalAnalyzer.Request(uri, debounce);
        }

        /// <summary>
        /// Publishes an empty diagnostic list for a closed document.
        /// </summary>
        private void ClearPublishedDiagnostics(Uri uri)
        {
            this._diagnosticsPublisher.ClearDiagnostics(uri);
        }
    }
}
