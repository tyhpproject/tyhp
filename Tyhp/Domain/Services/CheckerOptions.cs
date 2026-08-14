namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Configuration options controlling type-checker behavior.
    /// </summary>
    /// <remarks>
    /// Null safety and required type annotations are unconditional language rules — there are no
    /// toggles that relax them. Options here are either resource limits, target-environment
    /// gates, or explicit opt-ins for experimental emit features.
    /// </remarks>
    public class CheckerOptions
    {
        /// <summary>
        /// When true, <c>eval()</c> usage does not produce a diagnostic.
        /// Mirrors <c>build.allowEval</c>.
        /// </summary>
        public bool AllowEval { get; set; } = false;

        /// <summary>
        /// Maximum checker errors reported per source file before further errors are suppressed.
        /// Use 0 to disable the limit.
        /// </summary>
        // PLACEHOLDER_STORY_10: Read checker.maxErrorsPerFile from Project config
        public int MaxErrorsPerFile { get; set; } = 100;

        /// <summary>
        /// Upper bound on template-string automaton complexity for subtyping/inclusion checks.
        /// When exceeded, the checker is conservative (treats inclusion as unprovable) and emits a diagnostic.
        /// </summary>
        public int TemplateStringMaxStates { get; set; } = 256;

        /// <summary>
        /// Target PHP version used for version-gated checks (e.g. <c>with</c> on readonly).
        /// Defaults to <c>8.4</c>.
        /// </summary>
        public string PhpVersion { get; set; } = "8.4";

        /// <summary>
        /// Opt-in for anonymous-class <c>clone ... with</c> on readonly properties when targeting PHP &lt; 8.5.
        /// Mirrors <c>build.experimentalReadonlyCloneWith</c>.
        /// </summary>
        public bool ExperimentalReadonlyCloneWith { get; set; } = false;

        /// <summary>
        /// Creates checker options from project configuration.
        /// </summary>
        public static CheckerOptions FromProject(Tyhp.Config.Project project)
        {
            return new CheckerOptions
            {
                AllowEval = project.Build.AllowEval,
                TemplateStringMaxStates = project.Checker.TemplateStringMaxStates,
                PhpVersion = project.PhpVersion,
                ExperimentalReadonlyCloneWith = project.Build.ExperimentalReadonlyCloneWith,
            };
        }
    }
}
