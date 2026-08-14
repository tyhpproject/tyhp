using Microsoft.Extensions.Configuration;
using Tyhp.Extensions;

namespace Tyhp.Config
{
    /// <summary>
    /// Checker-specific configuration from <c>tyhp.json</c> <c>checker.*</c> keys.
    /// </summary>
    public sealed class CheckerConfig
    {
        /// <summary>
        /// Upper bound on template-string automaton complexity for subtyping/inclusion checks.
        /// </summary>
        public int TemplateStringMaxStates { get; set; } = 256;

        /// <summary>Maximum auto-fix re-run iterations for <c>tyhp lint --fix</c>.</summary>
        public int MaxFixIterations { get; set; } = 10;

        internal void ApplyFrom(IConfiguration configuration)
        {
            if (configuration.GetSection("checker:templateStringMaxStates").Exists()
                && Int32.TryParse(configuration["checker:templateStringMaxStates"], out int templateStringMaxStates)
                && templateStringMaxStates > 0)
            {
                this.TemplateStringMaxStates = templateStringMaxStates;
            }

            // CLI --max-fix-iterations wins when present; otherwise checker.maxFixIterations.
            if (configuration.GetSection("max-fix-iterations").Exists()
                && Int32.TryParse(configuration["max-fix-iterations"], out int cliMaxFixIterations)
                && cliMaxFixIterations > 0)
            {
                this.MaxFixIterations = cliMaxFixIterations;
            }
            else if (configuration.GetSection("checker:maxFixIterations").Exists()
                && Int32.TryParse(configuration["checker:maxFixIterations"], out int maxFixIterations)
                && maxFixIterations > 0)
            {
                this.MaxFixIterations = maxFixIterations;
            }
        }
    }
}
