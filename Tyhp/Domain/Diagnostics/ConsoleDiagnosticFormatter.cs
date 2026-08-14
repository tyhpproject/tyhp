using System.Collections.Concurrent;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Default diagnostic formatter that outputs to the console using the existing
    /// Message class format: filename(line,column): error TYHP1001: message text
    /// </summary>
    /// <remarks>
    /// When <see cref="Quiet"/> is false, a rustc-style source snippet (caret/underline
    /// + secondary labels) is appended via <see cref="CLI.RichDiagnosticRenderer"/>.
    /// Quiet mode degrades to the single-line header only.
    /// </remarks>
    public class ConsoleDiagnosticFormatter : IDiagnosticFormatter
    {
        // Formatters are shared (see the static defaults on Diagnostic / DiagnosticBag), so the
        // cache has to tolerate diagnostics being formatted from more than one thread.
        private readonly ConcurrentDictionary<string, string[]?> _sourceLineCache =
            new(StringComparer.Ordinal);

        /// <summary>
        /// When true, emits only the single-line diagnostic header (no source underlines).
        /// </summary>
        public bool Quiet { get; }

        /// <summary>
        /// Initializes a new console formatter.
        /// </summary>
        /// <param name="quiet">When true, skip rich source snippets.</param>
        public ConsoleDiagnosticFormatter(bool quiet = false)
        {
            this.Quiet = quiet;
        }

        /// <inheritdoc/>
        public void Format(IDiagnostic diagnostic)
        {
            try
            {
                CLI.RichDiagnosticRenderer.Write(
                    diagnostic,
                    this.Quiet,
                    this.TryGetSourceLine);
            }
            catch (Exception ex)
            {
                // Prevent formatter failures from crashing compilation
                CLI.Message.Error("CLI_FormatDiagnosticFailed", diagnostic.Code, ex.Message);
            }
        }

        /// <inheritdoc/>
        public void FormatSummary(DiagnosticBag bag)
        {
            try
            {
                if (bag.ErrorCount > 0)
                {
                    CLI.Message.Error("CLI_ErrorCountSummary", bag.ErrorCount);
                }

                if (bag.WarningCount > 0)
                {
                    CLI.Message.Warn("CLI_WarningCountSummary", bag.WarningCount);
                }

                if (bag.InfoCount > 0)
                {
                    CLI.Message.Info("CLI_InfoCountSummary", bag.InfoCount);
                }

                if (!bag.HasErrors && !bag.HasWarnings && bag.InfoCount == 0)
                {
                    CLI.Message.Success("CLI_NoDiagnostics");
                }
            }
            catch (Exception ex)
            {
                // Prevent formatter failures from crashing compilation
                CLI.Message.Error("CLI_FormatSummaryFailed", ex.Message);
            }
        }

        private string? TryGetSourceLine(string fileName, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName is "_" or "<input>"
                || lineNumber < 1)
            {
                return null;
            }

            var lines = this._sourceLineCache.GetOrAdd(fileName, this.TryReadAllLines);

            if (lines is null || lineNumber > lines.Length)
            {
                return null;
            }

            return lines[lineNumber - 1];
        }

        private string[]? TryReadAllLines(string fileName)
        {
            try
            {
                if (!File.Exists(fileName))
                {
                    return null;
                }

                return File.ReadAllLines(fileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
