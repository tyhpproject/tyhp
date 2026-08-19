namespace Tyhp.LanguageServer.Workspace
{
    using System.Collections.Concurrent;
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.LanguageServer.Analysis;

    /// <summary>
    /// Central registry for documents tracked by the language server.
    /// </summary>
    public sealed class WorkspaceManager : IDisposable
    {
        public const string LanguageModeTyhp = "tyhp";
        public const string LanguageModePhp = "php";
        public const string LanguageModeTyhpdef = "tyhpdef";

        private readonly ConcurrentDictionary<string, DocumentState> _documents = new(StringComparer.Ordinal);
        private int _disposed;

        /// <summary>
        /// Workspace root from the client's <c>initialize</c> <c>rootUri</c>/<c>rootPath</c>.
        /// Used by Phase 3 to scan project files.
        /// </summary>
        public string? WorkspaceRoot { get; set; }

        /// <summary>
        /// Creates a new tracked document, or replaces an already-open document at the same URI.
        /// </summary>
        public DocumentState OpenDocument(Uri uri, string content, int version)
        {
            this.ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentNullException.ThrowIfNull(content);

            string key = ToKey(uri);
            string filePath = ResolveFilePath(uri);
            var state = new DocumentState(
                uri,
                filePath,
                DetectLanguageMode(filePath),
                version,
                content)
            {
                IsDirty = true,
            };

            return this._documents.AddOrUpdate(
                key,
                state,
                (_, existing) =>
                {
                    existing.CancelAnalysis();
                    return state;
                });
        }

        /// <summary>
        /// Applies incremental (or full-document) LSP content changes to a tracked document.
        /// Returns null if the URI is not tracked.
        /// </summary>
        public DocumentState? UpdateDocument(
            Uri uri,
            IEnumerable<TextDocumentContentChangeEvent> changes,
            int version)
        {
            this.ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentNullException.ThrowIfNull(changes);

            if (!this._documents.TryGetValue(ToKey(uri), out DocumentState? state))
            {
                return null;
            }

            state.CancelAnalysis();
            lock (state.SyncRoot)
            {
                state.Content = ApplyIncrementalChanges(state.Content, changes);
                state.Version = version;
                state.IsDirty = true;
                state.ParsedAst = null;
            }

            return state;
        }

        /// <summary>
        /// Removes a document from tracking and cancels in-progress analysis.
        /// </summary>
        public bool CloseDocument(Uri uri)
        {
            this.ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(uri);

            if (!this._documents.TryRemove(ToKey(uri), out DocumentState? state))
            {
                return false;
            }

            state.CancelAnalysis();
            lock (state.SyncRoot)
            {
                state.ParsedAst = null;
                state.Diagnostics = Array.Empty<IDiagnostic>();
                state.Content = string.Empty;
                state.IsDirty = false;
            }

            return true;
        }

        /// <summary>Retrieves the current state, or null if the URI is not tracked.</summary>
        public DocumentState? GetDocument(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return this._documents.TryGetValue(ToKey(uri), out DocumentState? state) ? state : null;
        }

        /// <summary>Returns a snapshot of every tracked document.</summary>
        public IReadOnlyList<DocumentState> GetAllDocuments()
            => [.. this._documents.Values];

        /// <summary>Returns tracked documents whose <see cref="DocumentState.LanguageMode"/> matches <paramref name="mode"/>.</summary>
        public IReadOnlyList<DocumentState> GetDocumentsByLanguageMode(string mode)
        {
            ArgumentNullException.ThrowIfNull(mode);
            return [.. this._documents.Values.Where(document =>
                string.Equals(document.LanguageMode, mode, StringComparison.Ordinal))];
        }

        /// <summary>
        /// Applies LSP incremental text changes to a string. Range-based edits use 0-based
        /// lines and UTF-16 character offsets (LSP). A change with a null range replaces
        /// the entire document. Multiple changes in one notification are applied sequentially
        /// — each range refers to the document after the previous change.
        /// </summary>
        public static string ApplyIncrementalChanges(
            string currentContent,
            IEnumerable<TextDocumentContentChangeEvent> changes)
        {
            ArgumentNullException.ThrowIfNull(currentContent);
            ArgumentNullException.ThrowIfNull(changes);

            string content = currentContent;
            foreach (TextDocumentContentChangeEvent change in changes)
            {
                if (change is null)
                {
                    continue;
                }

                string replacement = change.Text ?? string.Empty;
                if (change.Range is null || change.Range.Start is null || change.Range.End is null)
                {
                    content = replacement;
                    continue;
                }

                int start = GetUtf16Offset(content, change.Range.Start);
                int end = GetUtf16Offset(content, change.Range.End);
                if (end < start)
                {
                    end = start;
                }

                content = string.Concat(content.AsSpan(0, start), replacement, content.AsSpan(end));
            }

            return content;
        }

        /// <summary>
        /// Language mode from file extension. <c>.tyhpdef</c> is checked before <c>.tyhp</c>
        /// because the former also ends with the latter.
        /// </summary>
        public static string DetectLanguageMode(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            if (filePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return LanguageModeTyhpdef;
            }

            if (filePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            {
                return LanguageModeTyhp;
            }

            return LanguageModePhp;
        }

        /// <summary>Resolves a document URI to a filesystem path when possible.</summary>
        public static string ResolveFilePath(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            if (uri.IsFile)
            {
                return uri.LocalPath;
            }

            return uri.OriginalString;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            foreach (DocumentState state in this._documents.Values)
            {
                state.CancelAnalysis();
            }

            this._documents.Clear();
        }

        /// <summary>
        /// Converts an LSP position (0-based line, UTF-16 character) to a UTF-16 offset
        /// in <paramref name="text"/>. Line endings are <c>\n</c>, <c>\r\n</c>, or <c>\r</c>.
        /// Positions past the end of a line or the document are clamped.
        /// </summary>
        internal static int GetUtf16Offset(string text, Position position)
            => PositionUtilities.GetOffset(text, position);

        private static string ToKey(Uri uri)
        {
            if (uri.IsAbsoluteUri)
            {
                return uri.AbsoluteUri;
            }

            return uri.ToString();
        }

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(this._disposed != 0, this);
    }
}
