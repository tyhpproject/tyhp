using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Lint.Fixes
{
    /// <summary>
    /// Stub: sorts <c>use</c> imports into a canonical order.
    /// </summary>
    /// <remarks>
    /// Provisional <see cref="TargetCode"/> until the binder emits a dedicated
    /// import-ordering diagnostic; <see cref="MessageCode.CheckerDuplicateImport"/>
    /// is import-related and unused by the other stubs.
    /// </remarks>
    public sealed class SortImportsFix : ILintFix
    {
        public MessageCode TargetCode => MessageCode.CheckerDuplicateImport;

        public string Description => "Sort imports";

        public LintFixResult Apply(string sourceText, IDiagnostic diagnostic)
        {
            // PLACEHOLDER_STORY_02: Implement when binder provides import ordering
            _ = sourceText;
            _ = diagnostic;
            return LintFixResult.Failed("Not yet implemented");
        }
    }
}
