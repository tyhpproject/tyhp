namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Strategy interface for formatting diagnostic output.
    /// Implementations can produce console output, JSON, SARIF, or other formats.
    /// </summary>
    /// <remarks>
    /// <para><b>Coordinate System Contract:</b></para>
    /// <para>
    /// Diagnostics received by formatters use Tyhp's internal coordinate system:
    /// - <b>Line numbers:</b> 1-based (line 1 is the first line, matching editor display)
    /// - <b>Column numbers:</b> 0-based (column 0 is the first character)
    /// </para>
    /// <para>
    /// Formatters MUST convert coordinates to match their target format's requirements:
    /// - <b>Console output:</b> Use coordinates as-is (human-readable 1-based lines)
    /// - <b>LSP/JSON output:</b> Convert line numbers to 0-based (line - 1)
    /// - <b>SARIF output:</b> Use 1-based for both lines and columns (column + 1)
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Formatters should catch and handle exceptions gracefully rather than
    /// crashing the compilation process. Use <see cref="CLI.Message"/> to report formatting errors.
    /// </para>
    /// </remarks>
    public interface IDiagnosticFormatter
    {
        /// <summary>
        /// Formats and outputs a single diagnostic message.
        /// </summary>
        /// <param name="diagnostic">The diagnostic to format. Line numbers are 1-based, columns are 0-based.</param>
        /// <remarks>
        /// Implementations must convert coordinates to match their output format's requirements.
        /// Should handle exceptions gracefully and not crash on malformed diagnostic data.
        /// </remarks>
        void Format(IDiagnostic diagnostic);

        /// <summary>
        /// Formats and outputs a summary of all diagnostics in a bag.
        /// </summary>
        /// <param name="bag">The diagnostic bag containing all diagnostics.</param>
        /// <remarks>
        /// Should handle exceptions gracefully and not crash on serialization failures.
        /// </remarks>
        void FormatSummary(DiagnosticBag bag);

        /// <summary>
        /// Supplies compilation context (file counts, phase timings) used by machine-readable
        /// formatters when emitting a summary. Default is a no-op for formatters that do not need it.
        /// </summary>
        /// <param name="result">The compilation result for the current pipeline run.</param>
        void SetContext(CompilationResult result)
        {
        }
    }
}
