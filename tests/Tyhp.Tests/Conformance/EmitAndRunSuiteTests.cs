using Tyhp.Tests.TestHelpers;
using Tyhp.Tests.TestHelpers.Conformance;

namespace Tyhp.Tests.Conformance;

[Trait("Category", "Conformance")]
[Trait("Category", "EmitAndRun")]
public class EmitAndRunSuiteTests
{
    public static IEnumerable<object[]> AllCases() => EmitAndRunRunner.DiscoverAllCases();

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EmitAndRunCase_MatchesManifest(string suiteId, string caseId)
        => EmitAndRunRunner.RunAndAssert(suiteId, caseId);

    [Fact]
    public void DiscoverAllCases_FindsSeedCorpus()
    {
        var ids = AllCases().Select(row => (string)row[1]).ToList();
        ids.Should().Contain("foreach_struct_property");
        ids.Should().Contain("foreach_missing_member");
        ids.Should().Contain("struct_extension_call");
        ids.Should().Contain("generic_mismatch");
        ids.Should().Contain("namespaced_toplevel_call");
        ids.Should().Contain("is_string_native");
        ids.Should().Contain("planted_arr_prop_fails_harness");
        ids.Should().Contain("parse_isolation_sibling_still_checked");
    }

    [Fact]
    public void GoldenPhpConformance_DoesNotDiscoverEmitAndRunManifest()
    {
        TestFileManager.GetAllConformanceManifests()
            .Should()
            .NotContain(path =>
                path.Contains($"{Path.DirectorySeparatorChar}emit-and-run{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    "emit-and-run",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhpStrictInvocation_SetsLockedIniFlags()
    {
        PhpToolchain.StrictErrorReportingArguments.Should().Be("-d error_reporting=-1 -d display_errors=1");
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var script = Path.Combine(tempDir, "ini.php");
            File.WriteAllText(script, """
                <?php
                echo (string)\ini_get('error_reporting'), "\n", (string)\ini_get('display_errors');
                """);

            var result = PhpToolchain.RunPhpScriptStrict(script);
            result.ExitCode.Should().Be(0, result.StandardError);
            result.StandardError.Should().BeEmpty();
            var lines = result.StandardOutput.Replace("\r\n", "\n").Trim().Split('\n');
            lines.Should().HaveCount(2);
            lines[0].Should().Be("-1");
            lines[1].Should().Be("1");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}

[Trait("Category", "Conformance")]
[Trait("Category", "EmitAndRun")]
public class PhpHarnessClassifierTests
{
    [Theory]
    [InlineData(0, "", false)]
    [InlineData(0, "hello\n", false)]
    [InlineData(1, "", true)]
    [InlineData(0, "Warning: Attempt to read property \"prop\" on array in /tmp/arr_prop.php on line 3\n", true)]
    [InlineData(0, "PHP Notice: Something in /tmp/x.php on line 1\n", true)]
    [InlineData(0, "Deprecated: foo() in /tmp/x.php on line 1\n", true)]
    [InlineData(255, "Fatal error: Uncaught Error in /tmp/x.php:1\n", true)]
    [InlineData(255, "Parse error: syntax error, unexpected token in /tmp/x.php on line 1\n", true)]
    public void ClassifiesExitAndStderr(int exitCode, string stderr, bool expectedFailure)
        => PhpHarnessClassifier.IsFailure(exitCode, stderr).Should().Be(expectedFailure);

    [Fact]
    public void StdoutWarningWord_IsNotAFailure()
        => PhpHarnessClassifier.IsFailure(0, "").Should().BeFalse();
}
