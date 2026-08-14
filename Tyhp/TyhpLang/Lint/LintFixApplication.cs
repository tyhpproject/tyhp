using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Lint
{
    /// <summary>
    /// Records one attempted (or successful) application of an <see cref="ILintFix"/>
    /// to a diagnostic.
    /// </summary>
    public sealed class LintFixApplication
    {
        /// <summary>The diagnostic that triggered the fix.</summary>
        public required IDiagnostic Diagnostic { get; init; }

        /// <summary>The fix that was applied (or attempted).</summary>
        public required ILintFix Fix { get; init; }

        /// <summary>Outcome of <see cref="ILintFix.Apply"/>.</summary>
        public required LintFixResult Result { get; init; }

        /// <summary>
        /// Path of the timestamped backup created before writing, or <see langword="null"/>
        /// when the file was not modified.
        /// </summary>
        public string? BackupPath { get; init; }
    }

    /// <summary>
    /// Identity of a fixed diagnostic location, used for auto-fix loop detection.
    /// </summary>
    public readonly record struct LintFixLocationKey(
        string FileName,
        MessageCode Code,
        int Line,
        int Column);

    /// <summary>
    /// Outcome of a single <see cref="LintFixEngine.ApplyFixes"/> pass.
    /// </summary>
    public sealed class LintFixPassResult
    {
        /// <summary>Applications attempted during this pass (successful and failed).</summary>
        public required IReadOnlyList<LintFixApplication> Applications { get; init; }

        /// <summary>
        /// When <see langword="true"/>, a previously applied fix at the same location was
        /// reintroduced — the caller should stop iterating.
        /// </summary>
        public bool LoopDetected { get; init; }

        /// <summary>Location that triggered loop detection, when <see cref="LoopDetected"/>.</summary>
        public LintFixLocationKey? LoopLocation { get; init; }
    }
}
