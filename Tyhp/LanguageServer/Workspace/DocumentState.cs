namespace Tyhp.LanguageServer.Workspace
{
    using Tyhp.Domain.Diagnostics;
    using Tyhp.TyhpLang.Ast;

    /// <summary>
    /// Current state of a single document tracked by the language server.
    /// </summary>
    public sealed class DocumentState
    {
        internal DocumentState(
            Uri uri,
            string filePath,
            string languageMode,
            int version,
            string content)
        {
            this.Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            this.FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            this.LanguageMode = languageMode ?? throw new ArgumentNullException(nameof(languageMode));
            this.Version = version;
            this.Content = content ?? throw new ArgumentNullException(nameof(content));
            this.Diagnostics = Array.Empty<IDiagnostic>();
        }

        /// <summary>The document's LSP URI.</summary>
        public Uri Uri { get; }

        /// <summary>Resolved file system path (or the URI string when not a file URI).</summary>
        public string FilePath { get; }

        /// <summary>
        /// Language mode inferred from the file extension:
        /// <c>tyhp</c>, <c>php</c>, or <c>tyhpdef</c>.
        /// </summary>
        public string LanguageMode { get; }

        /// <summary>Document version from the client.</summary>
        public int Version { get; internal set; }

        /// <summary>Current full text content.</summary>
        public string Content { get; internal set; }

        /// <summary>
        /// Last successfully parsed AST. Null if not yet parsed or parse failed.
        /// </summary>
        public SrcFileAst? ParsedAst { get; internal set; }

        /// <summary>
        /// Diagnostics produced the last time this document was actually re-parsed. An analysis
        /// cycle only re-parses documents whose content changed, so this is what lets a cycle
        /// triggered by a different document still know about this one's parse-time errors
        /// (e.g. syntax errors) without re-parsing it.
        /// </summary>
        internal IReadOnlyList<IDiagnostic> ParseDiagnostics { get; set; } = Array.Empty<IDiagnostic>();

        /// <summary>Latest diagnostics from analysis.</summary>
        public IReadOnlyList<IDiagnostic> Diagnostics { get; internal set; }

        /// <summary>Content has changed since last analysis.</summary>
        public bool IsDirty { get; internal set; }

        /// <summary>When analysis last completed.</summary>
        public DateTime? LastAnalysisTime { get; internal set; }

        /// <summary>
        /// Cancels in-progress analysis when the document changes or closes.
        /// </summary>
        public CancellationTokenSource? AnalysisCancellation { get; internal set; }

        /// <summary>
        /// Result id of the last <c>textDocument/semanticTokens/full</c> (or applied delta)
        /// payload for this document. Used to honor <c>semanticTokens/full/delta</c>.
        /// </summary>
        internal string? SemanticTokensResultId { get; set; }

        /// <summary>Encoded semantic-token data matching <see cref="SemanticTokensResultId"/>.</summary>
        internal int[] SemanticTokensData { get; set; } = [];

        /// <summary>Per-document lock for content and analysis-state updates.</summary>
        internal object SyncRoot { get; } = new();

        /// <summary>
        /// Cancels and disposes <see cref="AnalysisCancellation"/> if one is present.
        /// </summary>
        internal void CancelAnalysis()
        {
            CancellationTokenSource? cts;
            lock (this.SyncRoot)
            {
                cts = this.AnalysisCancellation;
                this.AnalysisCancellation = null;
            }

            if (cts is null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            cts.Dispose();
        }
    }
}
