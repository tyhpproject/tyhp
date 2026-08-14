namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// A labeled secondary span attached to a diagnostic (e.g. "defined here").
    /// </summary>
    public readonly record struct DiagnosticLabel(DiagnosticSpan Span, string Message)
    {
        /// <summary>
        /// Creates a label with a short message at the given span.
        /// </summary>
        public static DiagnosticLabel Create(DiagnosticSpan span, string message)
            => new(span, message ?? string.Empty);
    }
}
