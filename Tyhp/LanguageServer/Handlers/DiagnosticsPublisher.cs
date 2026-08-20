namespace Tyhp.LanguageServer.Handlers
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Configuration;
    using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
    using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
    using TyhpSeverity = Tyhp.Domain.Diagnostics.DiagnosticSeverity;

    /// <summary>
    /// Maps Tyhp diagnostics to LSP diagnostics and publishes them to the client.
    /// </summary>
    public sealed class DiagnosticsPublisher
    {
        public const string DiagnosticSource = "tyhp";

        private readonly JsonRpc _jsonRpc;
        private readonly ServerConfiguration _configuration;

        public DiagnosticsPublisher(JsonRpc jsonRpc, ServerConfiguration configuration)
        {
            this._jsonRpc = jsonRpc ?? throw new ArgumentNullException(nameof(jsonRpc));
            this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Maps <paramref name="diagnostics"/> to LSP diagnostics and sends
        /// <c>textDocument/publishDiagnostics</c> for <paramref name="uri"/>.
        /// </summary>
        public void PublishDiagnostics(Uri uri, IReadOnlyList<IDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentNullException.ThrowIfNull(diagnostics);

            if (!this._configuration.EnableDiagnostics)
            {
                return;
            }

            this.Notify(uri, MapAll(diagnostics));
        }

        /// <summary>
        /// Publishes an empty diagnostic list for a closed (or cleared) document.
        /// </summary>
        public void ClearDiagnostics(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            this.Notify(uri, []);
        }

        /// <summary>
        /// Maps a single Tyhp diagnostic to the LSP diagnostic DTO.
        /// </summary>
        internal static LspDiagnostic ToLspDiagnostic(IDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            var lsp = new LspDiagnostic
            {
                Range = PositionUtilities.ToLspRange(diagnostic),
                Severity = ToLspSeverity(diagnostic.Severity),
                Code = (int)diagnostic.Code,
                Source = DiagnosticSource,
                Message = diagnostic.Message,
            };

            DiagnosticTag[]? tags = MapTags(diagnostic);
            if (tags is { Length: > 0 })
            {
                lsp.Tags = tags;
            }

            return lsp;
        }

        private void Notify(Uri uri, LspDiagnostic[] diagnostics)
        {
            var payload = new PublishDiagnosticParams
            {
                Uri = uri,
                Diagnostics = diagnostics,
            };

            _ = this.NotifyAsync(payload);
        }

        private async Task NotifyAsync(PublishDiagnosticParams payload)
        {
            try
            {
                await this._jsonRpc.NotifyWithParameterObjectAsync(
                    Methods.TextDocumentPublishDiagnosticsName,
                    payload).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException
                or ConnectionLostException
                or InvalidOperationException
                or IOException)
            {
            }
        }

        private static LspDiagnostic[] MapAll(IReadOnlyList<IDiagnostic> diagnostics)
        {
            var mapped = new LspDiagnostic[diagnostics.Count];
            for (int i = 0; i < diagnostics.Count; i++)
            {
                mapped[i] = ToLspDiagnostic(diagnostics[i]);
            }

            return mapped;
        }

        private static LspDiagnosticSeverity ToLspSeverity(TyhpSeverity severity)
        {
            return severity switch
            {
                TyhpSeverity.Error => LspDiagnosticSeverity.Error,
                TyhpSeverity.Warning => LspDiagnosticSeverity.Warning,
                TyhpSeverity.Info => LspDiagnosticSeverity.Information,
                TyhpSeverity.Hint => LspDiagnosticSeverity.Hint,
                _ => LspDiagnosticSeverity.Information,
            };
        }

        private static DiagnosticTag[]? MapTags(IDiagnostic diagnostic)
        {
            if (diagnostic.Code == MessageCode.CheckerDeprecatedUsage
                || diagnostic.Code == MessageCode.CheckerObsoleteUsage)
            {
                return [DiagnosticTag.Deprecated];
            }

            if (diagnostic.Code == MessageCode.CheckerUnusedImport)
            {
                return [DiagnosticTag.Unnecessary];
            }

            return null;
        }
    }
}
