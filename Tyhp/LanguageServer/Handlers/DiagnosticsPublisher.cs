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

            this.Notify(uri, MapAll(diagnostics, uri));
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
        /// <param name="diagnostic">The Tyhp diagnostic to map.</param>
        /// <param name="documentUri">
        /// URI of the document this diagnostic is published against. Used as the
        /// <c>relatedInformation</c> location when a label's file matches that document,
        /// so the IDE can click through without resolving a different path.
        /// </param>
        internal static LspDiagnostic ToLspDiagnostic(IDiagnostic diagnostic, Uri? documentUri = null)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            var lsp = new TyhpLspDiagnostic
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

            DiagnosticRelatedInformation[]? related = MapRelatedInformation(diagnostic, documentUri);
            if (related is { Length: > 0 })
            {
                lsp.RelatedInformation = related;
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

        private static LspDiagnostic[] MapAll(IReadOnlyList<IDiagnostic> diagnostics, Uri documentUri)
        {
            var mapped = new LspDiagnostic[diagnostics.Count];
            for (int i = 0; i < diagnostics.Count; i++)
            {
                mapped[i] = ToLspDiagnostic(diagnostics[i], documentUri);
            }

            return mapped;
        }

        /// <summary>
        /// Maps Tyhp diagnostic labels to LSP <c>relatedInformation</c>. Labels whose
        /// file cannot be turned into a URI are skipped so the primary diagnostic still
        /// publishes.
        /// </summary>
        private static DiagnosticRelatedInformation[]? MapRelatedInformation(
            IDiagnostic diagnostic,
            Uri? documentUri)
        {
            if (diagnostic.Labels is not { Count: > 0 })
            {
                return null;
            }

            var related = new List<DiagnosticRelatedInformation>(diagnostic.Labels.Count);
            foreach (DiagnosticLabel label in diagnostic.Labels)
            {
                Uri? uri = ToRelatedLocationUri(label.Span.FileName, documentUri);
                if (uri is null)
                {
                    continue;
                }

                related.Add(new DiagnosticRelatedInformation
                {
                    Location = new Location
                    {
                        Uri = uri,
                        Range = PositionUtilities.ToLspRange(label.Span),
                    },
                    Message = label.Message ?? string.Empty,
                });
            }

            return related.Count > 0 ? related.ToArray() : null;
        }

        /// <summary>
        /// Converts a label file path to an LSP document URI. Prefers
        /// <paramref name="documentUri"/> when it refers to the same path so click-through
        /// stays on the open buffer.
        /// </summary>
        private static Uri? ToRelatedLocationUri(string? fileName, Uri? documentUri)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName is "_" or "<input>")
            {
                return null;
            }

            if (documentUri is not null && PathsReferToSameDocument(fileName, documentUri))
            {
                return documentUri;
            }

            try
            {
                // Reject 1-character schemes (Windows drive letters parsed as URI schemes).
                if (Uri.TryCreate(fileName, UriKind.Absolute, out Uri? existing)
                    && existing.IsAbsoluteUri
                    && existing.Scheme.Length > 1)
                {
                    return existing;
                }

                return new Uri(Path.GetFullPath(fileName));
            }
            catch (Exception ex) when (
                ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or UriFormatException
                or IOException)
            {
                return null;
            }
        }

        private static bool PathsReferToSameDocument(string fileName, Uri documentUri)
        {
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            string documentPath = documentUri.IsFile ? documentUri.LocalPath : documentUri.OriginalString;
            string normalizedFile = fileName.Replace('\\', '/');
            string normalizedDocument = documentPath.Replace('\\', '/');
            if (string.Equals(fileName, documentPath, comparison)
                || string.Equals(fileName, documentUri.OriginalString, comparison)
                || string.Equals(normalizedFile, normalizedDocument, comparison))
            {
                return true;
            }

            if (normalizedDocument.EndsWith(normalizedFile, comparison)
                && (normalizedDocument.Length == normalizedFile.Length
                    || normalizedDocument[normalizedDocument.Length - normalizedFile.Length - 1] is '/' or '\\'))
            {
                return true;
            }

            try
            {
                string left = Path.GetFullPath(fileName);
                string right = documentUri.IsFile
                    ? Path.GetFullPath(documentUri.LocalPath)
                    : documentPath;
                if (string.Equals(left, right, comparison))
                {
                    return true;
                }
            }
            catch (Exception ex) when (
                ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException)
            {
            }

            return string.Equals(
                Path.GetFileName(fileName),
                Path.GetFileName(documentPath),
                comparison);
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
