using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Lint.Fixes
{
    /// <summary>
    /// Stub: removes an unused <c>use</c> import reported by the checker.
    /// </summary>
    public sealed class RemoveUnusedImportFix : ILintFix
    {
        public MessageCode TargetCode => MessageCode.CheckerUnusedImport;

        public string Description => "Remove unused import";

        public LintFixResult Apply(string sourceText, IDiagnostic diagnostic)
        {
            // PLACEHOLDER_STORY_02: Implement when binder identifies unused imports
            _ = sourceText;
            _ = diagnostic;
            return LintFixResult.Failed("Not yet implemented");
        }
    }
}
