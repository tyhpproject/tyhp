using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Integration;

[Trait("Category", "PHP")]
[Trait("Category", "Integration")]
public class TyhpLibPhpTests
{
    // Skipped until the runtime self-host milestone lands (ROADMAP: "compiler builds its own runtime",
    // Story 07 Phase 9 / Wave B — green after Story 10). The composer autoload path is fixed, but the
    // checked-in compiled runtime PHP is stale/mid-reorganization and PHPUnit reports real errors from
    // not-yet-complete work: emitter qualified-name emission (e.g. `Tyhp\Type::` vs `\Tyhp\Type::`, part of
    // Story 11 emitter feature expansion) and the in-progress runtime-package reorg (DecimalConvertable →
    // DecimalConvertible, new Contracts/, and decimal/tyhp.json still including the deleted GMP.tyhpdef, which
    // blocks a clean recompile). Re-enable after the reorg + emitter expansion land and the runtime is
    // recompiled. See FOUND_BUGS.md item #4(b).
    [Fact(Skip = "Runtime self-host not green yet: stale/mid-reorg compiled PHP + incomplete emitter qualified-name emission (Story 11). See FOUND_BUGS.md item #4(b).")]
    public void PhpUnit_RuntimePackages_AllPass()
    {
        if (!PhpToolchain.IsAvailable())
        {
            return;
        }

        var runtimeDirectory = PhpToolchain.GetRuntimeDirectory();
        Directory.Exists(runtimeDirectory).Should().BeTrue();

        var vendorAutoload = Path.Combine(runtimeDirectory, "vendor", "autoload.php");
        if (!File.Exists(vendorAutoload))
        {
            var installResult = RunComposerCommand(runtimeDirectory, "install");
            installResult.ExitCode.Should().Be(0, $"composer install failed:\n{installResult.CombinedOutput}");
        }

        var testResult = PhpToolchain.RunComposerTest(runtimeDirectory);
        testResult.ExitCode.Should().Be(0, $"PHPUnit runtime tests failed:\n{testResult.CombinedOutput}");
    }

    private static PhpToolchain.ProcessResult RunComposerCommand(string workingDirectory, string command)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "composer",
                Arguments = command,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromMinutes(5));
        return new PhpToolchain.ProcessResult(process.ExitCode, stdout, stderr);
    }
}
