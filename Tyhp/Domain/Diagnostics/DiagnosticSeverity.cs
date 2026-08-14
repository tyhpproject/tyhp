namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Represents the severity level of a diagnostic message.
    /// </summary>
    public enum DiagnosticSeverity
    {
        /// <summary>
        /// Compilation fails - code cannot be executed.
        /// </summary>
        Error,

        /// <summary>
        /// Compilation succeeds but code is suspicious or potentially incorrect.
        /// </summary>
        Warning,

        /// <summary>
        /// Informational message (style, deprecation, etc.).
        /// </summary>
        Info,

        /// <summary>
        /// Low-priority suggestion for future LSP use.
        /// </summary>
        Hint
    }
}
