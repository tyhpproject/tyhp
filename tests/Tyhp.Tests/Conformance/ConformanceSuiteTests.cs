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
    [Fact(Skip = "C# tests do not load runtime packages. Golden self-host belongs with runtime/packages/test-all-tyhpdef.sh (FOUND_BUGS.md item #1).")]
    public void SelfHost_RecompiledRuntime_MatchesCommittedPhp()
    {
    }
}
