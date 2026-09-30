using System.Text.RegularExpressions;

namespace Tyhp.Tests.TestHelpers.Conformance;

/// <summary>
/// Classifies PHP CLI stderr for the emit-and-run harness. A program that type-checks
/// and then warns or fatals under PHP is a silent wrong-emit.
/// </summary>
public static class PhpHarnessClassifier
{
    private static readonly Regex FailurePattern = new(
        @"\b(Warning|Notice|Deprecated|Fatal error|Parse error)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsFailure(int exitCode, string? stderr)
        => exitCode != 0 || FailurePattern.IsMatch(stderr ?? string.Empty);
}
