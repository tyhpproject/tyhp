using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Lint.Fixes
{
    /// <summary>
    /// Stub: adds a missing type annotation when the checker can infer the type.
    /// </summary>
    public sealed class AddMissingTypeAnnotationFix : ILintFix
    {
        // TypeAnnotationRule reports this for typed vars, parameters, and return types that
        // lack an annotation — the diagnostic this fix will resolve once Story 08 supplies
        // the inferred type.
        public MessageCode TargetCode => MessageCode.CheckerVariableTypeRequired;

        public string Description => "Add missing type annotation";

        public LintFixResult Apply(string sourceText, IDiagnostic diagnostic)
        {
            // PLACEHOLDER_STORY_08: Implement when checker identifies inferable types
            _ = sourceText;
            _ = diagnostic;
            return LintFixResult.Failed("Not yet implemented");
        }
    }
}
