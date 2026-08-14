using Tyhp.Tests.TestHelpers;
using Tyhp.Tests.TestHelpers.Conformance;

namespace Tyhp.Tests.Conformance;

[Trait("Category", "Conformance")]
public class ConformanceSuiteTests
{
    public static IEnumerable<object[]> AllCases() => ConformanceRunner.DiscoverAllCases();

    [Theory]
    [MemberData(nameof(AllCases))]
    public void ConformanceCase_MatchesManifest(string suiteId, string caseId)
        => ConformanceRunner.RunAndAssert(suiteId, caseId);
}

[Trait("Category", "Conformance")]
public class SelfHostRuntimeConformanceTests
{
    [Fact(Skip = "Kept skipped (low investment, 2026-08-12): golden compare still targets packages/*/src after packages→dist; retarget or move to runtime-repo CI later. See FOUND_BUGS.md Suite-reds 2026-08-03 item #1.")]
    public void SelfHost_RecompiledRuntime_MatchesCommittedPhp()
    {
        var results = SelfHostRunner.VerifyAllPackages();
        results.Should().NotBeEmpty();

        var allowlist = SelfHostRunner.ExpectedToCompileAllowlist;
        var failures = new List<string>();

        foreach (var packageName in allowlist)
        {
            var result = results.SingleOrDefault(r =>
                string.Equals(r.PackageName, packageName, StringComparison.OrdinalIgnoreCase));
            result.Should().NotBeNull($"allowlisted package '{packageName}' should be verified");
            if (result is null)
            {
                continue;
            }

            if (SelfHostRunner.IsBuildFailure(result))
            {
                failures.Add(
                    $"- {result.PackageName} (allowlisted): expected to compile but build failed\n"
                    + $"  {string.Join("\n  ", result.Details)}");
                continue;
            }

            if (!result.Succeeded)
            {
                failures.Add(
                    $"- {result.PackageName} (allowlisted): {result.Summary}\n"
                    + $"  {string.Join("\n  ", result.Details)}");
            }
        }

        foreach (var result in results.Where(SelfHostRunner.CompiledSuccessfully))
        {
            // Allowlisted packages are already reported (with their build-failure / mismatch
            // detail) by the allowlist loop above; skip them here to avoid duplicate failures.
            if (allowlist.Contains(result.PackageName))
            {
                continue;
            }

            if (!result.Succeeded)
            {
                failures.Add(
                    $"- {result.PackageName}: compiled but PHP output differs from committed src/\n"
                    + $"  {string.Join("\n  ", result.Details)}");
            }
        }

        foreach (var result in results.Where(SelfHostRunner.IsInfrastructureFailure))
        {
            failures.Add($"- {result.PackageName}: {result.Summary}");
        }

        var anyCompiled = results.Any(SelfHostRunner.CompiledSuccessfully);
        if (!anyCompiled && allowlist.Count == 0 && failures.Count == 0)
        {
            // SelfHostRunner infrastructure is active; no runtime package compiles yet.
            return;
        }

        failures.Should().BeEmpty(
            "runtime self-host conformance failures:\n" + string.Join(Environment.NewLine, failures));
    }
}
