using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Lint
{
    /// <summary>
    /// A code transformation that resolves a specific diagnostic (used by <c>tyhp lint --fix</c>).
    /// </summary>
    public interface ILintFix
    {
        /// <summary>The diagnostic code this fix addresses.</summary>
        MessageCode TargetCode { get; }

        /// <summary>Human-readable description of the fix (e.g. "Add missing type annotation").</summary>
        string Description { get; }

        /// <summary>
        /// Apply the fix to <paramref name="sourceText"/> for the given diagnostic.
        /// </summary>
        /// <returns>Success with modified text and edits, or failure with a reason.</returns>
        LintFixResult Apply(string sourceText, IDiagnostic diagnostic);
    }
}
