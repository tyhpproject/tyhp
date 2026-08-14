using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Immutable diagnostic record representing an error, warning, info, or hint message
    /// from any compiler phase. Implements value equality for testing.
    /// </summary>
    public record class Diagnostic : IDiagnostic
    {
        /// <inheritdoc/>
        public DiagnosticSeverity Severity { get; init; }

        /// <inheritdoc/>
        public MessageCode Code { get; init; }

        /// <inheritdoc/>
        public string FileName { get; init; }

        /// <inheritdoc/>
        public int Line { get; init; }

        /// <inheritdoc/>
        public int Column { get; init; }

        /// <inheritdoc/>
        public int? EndLine { get; init; }

        /// <inheritdoc/>
        public int? EndColumn { get; init; }

        /// <inheritdoc/>
        public object[] FormatParams { get; init; }

        /// <inheritdoc/>
        public bool HasSourcePosition { get; init; }

        /// <inheritdoc/>
        public IReadOnlyList<DiagnosticLabel> Labels { get; init; }

        /// <inheritdoc/>
        public string? Help { get; init; }

        /// <inheritdoc/>
        public string? Note { get; init; }

        /// <inheritdoc/>
        public DiagnosticSuggestion? Suggestion { get; init; }

        private string? _cachedMessage;

        /// <inheritdoc/>
        public string Message
        {
            get
            {
                if (this._cachedMessage == null)
                {
                    this._cachedMessage = this.Severity switch
                    {
                        DiagnosticSeverity.Error => CLI.Message.LocalizeErrorCode((int)this.Code, this.FormatParams),
                        DiagnosticSeverity.Warning => CLI.Message.LocalizeWarningCode((int)this.Code, this.FormatParams),
                        DiagnosticSeverity.Info => CLI.Message.LocalizeInfoCode((int)this.Code, this.FormatParams),
                        DiagnosticSeverity.Hint => CLI.Message.LocalizeInfoCode((int)this.Code, this.FormatParams),
                        _ => $"Unknown severity: {this.Severity}"
                    };
                }
                return this._cachedMessage;
            }
        }

        /// <summary>
        /// The primary source span for this diagnostic.
        /// </summary>
        public DiagnosticSpan PrimarySpan
            => DiagnosticSpan.FromDiagnostic(this);

        /// <summary>
        /// Initializes a new diagnostic with all properties.
        /// </summary>
        /// <param name="severity">The severity level of the diagnostic.</param>
        /// <param name="code">The message code identifying the diagnostic type.</param>
        /// <param name="fileName">The source file where the diagnostic was reported.</param>
        /// <param name="line">The line number where the diagnostic was reported (1-indexed, minimum 1).</param>
        /// <param name="column">The column position where the diagnostic was reported (0-indexed, minimum 0).</param>
        /// <param name="formatParams">Parameters for formatting the localized message.</param>
        /// <param name="endLine">Optional ending line number for multi-line spans (minimum 1).</param>
        /// <param name="endColumn">Optional ending column position for multi-line spans (minimum 0).</param>
        /// <param name="labels">Optional labeled secondary spans.</param>
        /// <param name="help">Optional help prose.</param>
        /// <param name="note">Optional note prose.</param>
        /// <param name="suggestion">Optional machine-applicable edit suggestion.</param>
        /// <remarks>
        /// Line and column values are validated and clamped to valid ranges:
        /// - Line numbers are clamped to a minimum of 1 (1-indexed)
        /// - Column numbers are clamped to a minimum of 0 (0-indexed)
        /// This ensures LSP compatibility when converting to 0-based coordinates.
        /// </remarks>
        public Diagnostic(
            DiagnosticSeverity severity,
            MessageCode code,
            string fileName,
            int line,
            int column,
            object[] formatParams,
            int? endLine = null,
            int? endColumn = null,
            IReadOnlyList<DiagnosticLabel>? labels = null,
            string? help = null,
            string? note = null,
            DiagnosticSuggestion? suggestion = null)
        {
            this.Severity = severity;
            this.Code = code;
            this.FileName = fileName ?? string.Empty;
            // Line 0 is the producers' "no position in this file" marker; record it before the
            // clamp below erases it so renderers know there is nothing to underline.
            this.HasSourcePosition = line >= 1;
            // Clamp line to minimum of 1 (1-indexed line numbers)
            this.Line = Math.Max(1, line);
            // Clamp column to minimum of 0 (0-indexed column positions)
            this.Column = Math.Max(0, column);
            // Clamp endLine to minimum of 1 if provided
            this.EndLine = endLine.HasValue ? Math.Max(1, endLine.Value) : null;
            // Clamp endColumn to minimum of 0 if provided
            this.EndColumn = endColumn.HasValue ? Math.Max(0, endColumn.Value) : null;
            this.FormatParams = formatParams ?? Array.Empty<object>();
            this.Labels = labels is { Count: > 0 }
                ? labels
                : Array.Empty<DiagnosticLabel>();
            this.Help = help;
            this.Note = note;
            this.Suggestion = suggestion;
        }

        /// <summary>
        /// Creates an error diagnostic.
        /// </summary>
        public static Diagnostic Error(
            MessageCode code,
            string fileName,
            int line,
            int column,
            object[] formatParams,
            int? endLine = null,
            int? endColumn = null,
            IReadOnlyList<DiagnosticLabel>? labels = null,
            string? help = null,
            string? note = null,
            DiagnosticSuggestion? suggestion = null)
        {
            return new Diagnostic(
                DiagnosticSeverity.Error,
                code,
                fileName,
                line,
                column,
                formatParams,
                endLine,
                endColumn,
                labels,
                help,
                note,
                suggestion);
        }

        /// <summary>
        /// Creates a warning diagnostic.
        /// </summary>
        public static Diagnostic Warning(
            MessageCode code,
            string fileName,
            int line,
            int column,
            object[] formatParams,
            int? endLine = null,
            int? endColumn = null,
            IReadOnlyList<DiagnosticLabel>? labels = null,
            string? help = null,
            string? note = null,
            DiagnosticSuggestion? suggestion = null)
        {
            return new Diagnostic(
                DiagnosticSeverity.Warning,
                code,
                fileName,
                line,
                column,
                formatParams,
                endLine,
                endColumn,
                labels,
                help,
                note,
                suggestion);
        }

        /// <summary>
        /// Creates an info diagnostic.
        /// </summary>
        public static Diagnostic Info(
            MessageCode code,
            string fileName,
            int line,
            int column,
            object[] formatParams,
            int? endLine = null,
            int? endColumn = null,
            IReadOnlyList<DiagnosticLabel>? labels = null,
            string? help = null,
            string? note = null,
            DiagnosticSuggestion? suggestion = null)
        {
            return new Diagnostic(
                DiagnosticSeverity.Info,
                code,
                fileName,
                line,
                column,
                formatParams,
                endLine,
                endColumn,
                labels,
                help,
                note,
                suggestion);
        }

        /// <summary>
        /// Creates a hint diagnostic for low-priority suggestions (future LSP use).
        /// </summary>
        public static Diagnostic Hint(
            MessageCode code,
            string fileName,
            int line,
            int column,
            object[] formatParams,
            int? endLine = null,
            int? endColumn = null,
            IReadOnlyList<DiagnosticLabel>? labels = null,
            string? help = null,
            string? note = null,
            DiagnosticSuggestion? suggestion = null)
        {
            return new Diagnostic(
                DiagnosticSeverity.Hint,
                code,
                fileName,
                line,
                column,
                formatParams,
                endLine,
                endColumn,
                labels,
                help,
                note,
                suggestion);
        }

        /// <summary>
        /// Returns a copy of this diagnostic with the given secondary labels.
        /// </summary>
        public Diagnostic WithLabels(params DiagnosticLabel[] labels)
            => this with { Labels = labels ?? Array.Empty<DiagnosticLabel>() };

        /// <summary>
        /// Returns a copy of this diagnostic with help prose.
        /// </summary>
        public Diagnostic WithHelp(string? help)
            => this with { Help = help };

        /// <summary>
        /// Returns a copy of this diagnostic with note prose.
        /// </summary>
        public Diagnostic WithNote(string? note)
            => this with { Note = note };

        /// <summary>
        /// Returns a copy of this diagnostic with a machine-applicable suggestion.
        /// </summary>
        public Diagnostic WithSuggestion(DiagnosticSuggestion? suggestion)
            => this with { Suggestion = suggestion };

        private static readonly ConsoleDiagnosticFormatter _defaultFormatter = new();

        /// <inheritdoc/>
        public void Display(IDiagnosticFormatter? formatter = null)
        {
            formatter ??= _defaultFormatter;
            formatter.Format(this);
        }
    }
}
