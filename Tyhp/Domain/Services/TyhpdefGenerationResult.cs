using Tyhp.Domain.Diagnostics;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Outcome of a tyhpdef generation run (Reflection, PHP-source harvest, or compiled Tyhp).
    /// </summary>
    public sealed class TyhpdefGenerationResult
    {
        /// <summary>Diagnostics produced during generation.</summary>
        public DiagnosticBag Diagnostics { get; } = new();

        /// <summary>Paths of generated tyhpdef files.</summary>
        public List<string> GeneratedFiles { get; } = [];

        /// <summary>Count of declarations generated.</summary>
        public int TotalDeclarations { get; set; }

        /// <summary>Classes, interfaces, traits, and enums generated.</summary>
        public int ClassCount { get; set; }

        /// <summary>Functions generated.</summary>
        public int FunctionCount { get; set; }

        /// <summary>Constants generated.</summary>
        public int ConstantCount { get; set; }

        /// <summary>How long generation took.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary><c>--validate</c> files discovered.</summary>
        public int ValidatedFileCount { get; set; }

        /// <summary><c>--validate</c> files that parsed.</summary>
        public int ValidatedPassedCount { get; set; }

        /// <summary><c>--validate</c> files that failed to parse.</summary>
        public int ValidatedFailedCount { get; set; }

        /// <summary>Total parse errors across <c>--validate</c> files.</summary>
        public int ValidatedErrorCount { get; set; }

        /// <summary>Markdown from <c>--audit-stubs</c>.</summary>
        public string? AuditReport { get; set; }

        /// <summary>
        /// Localized PHP-source harvest notices (missing required wrappers, omitted
        /// extends-extern types). Also printed via <c>Message.Warn</c>.
        /// </summary>
        public List<string> Warnings { get; } = [];

        /// <summary>True when no error-severity diagnostics were recorded.</summary>
        public bool Success => !this.Diagnostics.HasErrors;
    }
}
