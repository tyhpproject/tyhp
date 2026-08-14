namespace Tyhp.CLI.IntegrityChecks
{
    /// <summary>
    /// One validation step run by <see cref="IntegrityCheckAction"/>.
    /// </summary>
    public interface IIntegrityCheck
    {
        /// <summary>Human-readable check name (already localized).</summary>
        string Name { get; }

        /// <summary>Execute the check.</summary>
        Task<IntegrityCheckResult> RunAsync(CancellationToken ct);
    }

    /// <summary>
    /// Outcome of a single integrity check.
    /// </summary>
    public sealed class IntegrityCheckResult
    {
        public bool Passed { get; init; }

        /// <summary>Short summary (already localized), shown beside the check name.</summary>
        public string? Message { get; init; }

        /// <summary>
        /// The specific reasons the check did not pass (already localized). Always shown, so a
        /// failure is actionable without re-running with <c>--verbose</c>.
        /// </summary>
        public List<string> Problems { get; init; } = [];

        /// <summary>Extra lines shown when <c>--verbose</c> is set (already localized).</summary>
        public List<string> Details { get; init; } = [];

        /// <summary>
        /// When <see cref="Passed"/> is false, display with <see cref="Message.Warn"/> instead of
        /// <see cref="Message.Error"/> (still counts as a failed check for the exit code).
        /// </summary>
        public bool IsWarning { get; init; }

        public static IntegrityCheckResult Pass(string? message = null, IEnumerable<string>? details = null)
            => new()
            {
                Passed = true,
                Message = message,
                Details = details?.ToList() ?? [],
            };

        public static IntegrityCheckResult Fail(
            string message,
            IEnumerable<string>? details = null,
            IEnumerable<string>? problems = null,
            bool isWarning = false)
            => new()
            {
                Passed = false,
                Message = message,
                Problems = problems?.ToList() ?? [],
                Details = details?.ToList() ?? [],
                IsWarning = isWarning,
            };
    }
}
