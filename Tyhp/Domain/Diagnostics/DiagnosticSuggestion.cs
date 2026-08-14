namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// A machine-applicable edit suggestion (span + replacement text).
    /// Populated by Story 14 Phase 3 producers; schema and rendering contracts are defined here
    /// so JSON/SARIF consumers can already accept the field.
    /// </summary>
    public readonly record struct DiagnosticSuggestion(
        DiagnosticSpan Span,
        string Replacement,
        string? Description = null)
    {
        /// <summary>
        /// Creates a suggestion that replaces <paramref name="span"/> with <paramref name="replacement"/>.
        /// </summary>
        public static DiagnosticSuggestion Create(
            DiagnosticSpan span,
            string replacement,
            string? description = null)
            => new(span, replacement ?? string.Empty, description);
    }
}
