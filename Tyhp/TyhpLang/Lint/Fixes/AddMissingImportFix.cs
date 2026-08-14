using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Lint.Fixes
{
    /// <summary>
    /// Stub: inserts a missing <c>use</c> import when the binder suggests one.
    /// </summary>
    public sealed class AddMissingImportFix : ILintFix
    {
        public MessageCode TargetCode => MessageCode.BinderSymbolNotFound;

        public string Description => "Add missing import";

        public LintFixResult Apply(string sourceText, IDiagnostic diagnostic)
        {
            // PLACEHOLDER_STORY_02: Implement when binder reports missing imports with suggestions
            _ = sourceText;
            _ = diagnostic;
            return LintFixResult.Failed("Not yet implemented");
        }
    }
}
