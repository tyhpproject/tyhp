using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Represents a diagnostic message (error, warning, info, or hint) from any compiler phase.
    /// </summary>
    public interface IDiagnostic
    {
        /// <summary>
        /// Gets the severity level of this diagnostic.
        /// </summary>
        DiagnosticSeverity Severity { get; }

        /// <summary>
        /// Gets the message code identifying the type of diagnostic.
        /// </summary>
        MessageCode Code { get; }

        /// <summary>
        /// Gets the source file name where the diagnostic was reported.
        /// </summary>
        string FileName { get; }

        /// <summary>
        /// Gets the line number where the diagnostic was reported (1-indexed).
        /// </summary>
        int Line { get; }

        /// <summary>
        /// Gets the column position where the diagnostic was reported (0-indexed).
        /// </summary>
        int Column { get; }

        /// <summary>
        /// Gets the ending line number for multi-line diagnostic spans (optional, for IDE integration).
        /// </summary>
        int? EndLine { get; }

        /// <summary>
        /// Gets the ending column position for multi-line diagnostic spans (optional, for IDE integration).
        /// </summary>
        int? EndColumn { get; }

        /// <summary>
        /// Gets the localized, formatted human-readable message.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// Gets the format parameters used to construct the message.
        /// </summary>
        object[] FormatParams { get; }

        /// <summary>
        /// Gets whether the diagnostic points at a real position inside <see cref="FileName"/>.
        /// Producers report file-level diagnostics with line 0; <see cref="Line"/> is clamped to 1
        /// for LSP compatibility, so this flag is what tells a renderer there is nothing to underline.
        /// </summary>
        bool HasSourcePosition => true;

        /// <summary>
        /// Gets labeled secondary spans (e.g. "defined here"). Empty when none are attached.
        /// </summary>
        IReadOnlyList<DiagnosticLabel> Labels { get; }

        /// <summary>
        /// Gets optional help prose attached to this diagnostic.
        /// </summary>
        string? Help { get; }

        /// <summary>
        /// Gets optional note prose attached to this diagnostic.
        /// </summary>
        string? Note { get; }

        /// <summary>
        /// Gets an optional machine-applicable edit suggestion (Story 14 Phase 3 producers).
        /// </summary>
        DiagnosticSuggestion? Suggestion { get; }

        /// <summary>
        /// Outputs this diagnostic using the specified formatter.
        /// </summary>
        /// <param name="formatter">The formatter to use, or null to use the default console formatter.</param>
        void Display(IDiagnosticFormatter? formatter = null);
    }
}
