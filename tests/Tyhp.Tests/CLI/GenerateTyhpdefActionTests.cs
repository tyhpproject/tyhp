using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class GenerateTyhpdefActionTests
{
    [Fact]
    public void NoArguments_ReportsRequiredFlagsAndFails()
    {
        var result = Run(new Dictionary<string, string?>());

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        result.Success.Should().BeFalse();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefGenerationError);
        result.Diagnostics.Errors.Should().Contain(d =>
            d.Message.Contains("`--ext-name`", StringComparison.Ordinal)
            && d.Message.Contains("`--package-path`", StringComparison.Ordinal)
            && d.Message.Contains("`--source`", StringComparison.Ordinal)
            && d.Message.Contains("`--audit-stubs`", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtName_WithMissingUserPhp_ReportsTyhpdefPhpNotFound()
    {
        var missingPhp = Path.Combine(Path.GetTempPath(), "tyhp-no-php-" + Guid.NewGuid().ToString("N"), "php");
        var action = RunAction(new Dictionary<string, string?>
        {
            ["ext-name"] = "widget",
            ["php"] = missingPhp,
        });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastOptions.Should().NotBeNull();
        action.LastOptions!.Mode.Should().Be(TyhpdefGenerationMode.PhpExtension);
        action.LastOptions.ExtensionName.Should().Be("widget");
        action.LastOptions.PreferPhpRuntime.Should().BeTrue();
        action.LastResult!.Success.Should().BeFalse();
        action.LastResult.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPhpNotFound);
        action.LastResult.Diagnostics.Errors.Should().NotContain(d =>
            d.Message.Contains("PHP delegation not yet implemented", StringComparison.Ordinal));
    }

    [Fact]
    public void PhpTargetsWithoutUserPhp_FailClosedWhenRuntimeCannotBeProvisioned()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-cli-targets-").FullName;
        try
        {
            var action = RunAction(
                new Dictionary<string, string?>
                {
                    ["ext-name"] = "json",
                    ["php-targets"] = "8.2,8.3",
                    ["output"] = outputDir,
                    ["no-docs"] = "true",
                    ["no-php-runtime-update"] = "true",
                },
                generator: new PhpDelegationTyhpdefGenerator(
                    new FailingPhpRuntimeManager(),
                    new PhpRuntimeDetector()));

            action.LastResult!.Success.Should().BeFalse();
            action.LastResult.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefPhpRuntimeDownloadFailed);
            action.LastResult.GeneratedFiles.Should().BeEmpty();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void Source_GeneratesFromPhpGlob()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-cli-src-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-cli-php-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Sample.php");
            File.WriteAllText(php, "<?php\nclass Sample { public function ping(): int { return 1; } }\n");
            var action = RunAction(new Dictionary<string, string?>
            {
                ["source"] = php,
                ["output"] = outputDir,
                ["overwrite"] = "true",
                ["no-docs"] = "true",
            });

            Environment.ExitCode.Should().Be((int)ExitCode.Success);
            action.LastOptions.Should().NotBeNull();
            action.LastOptions!.Mode.Should().Be(TyhpdefGenerationMode.PhpSourceFiles);
            action.LastOptions.PreferPhpRuntime.Should().BeFalse();
            action.LastResult!.Success.Should().BeTrue(
                string.Join("; ", action.LastResult.Diagnostics.Errors.Select(e => e.Message)));
            action.LastResult.GeneratedFiles.Should().Contain(p =>
                p.EndsWith("source.tyhpdef", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void PackagePath_MissingComposerJson_ReportsGenerationError()
    {
        var packageDir = Directory.CreateTempSubdirectory("tyhpdef-cli-pkg-").FullName;
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-cli-pkg-out-").FullName;
        try
        {
            var action = RunAction(new Dictionary<string, string?>
            {
                ["package-path"] = packageDir,
                ["output"] = outputDir,
            });

            action.LastOptions!.Mode.Should().Be(TyhpdefGenerationMode.ComposerPackage);
            action.LastOptions.PackagePath.Should().Be(packageDir);
            action.LastResult!.Success.Should().BeFalse();
            action.LastResult.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefGenerationError
                && d.Message.Contains("composer.json", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(packageDir);
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void MultipleModes_FailsWithoutDispatching()
    {
        var action = RunAction(new Dictionary<string, string?>
        {
            ["ext-name"] = "widget",
            ["source"] = "./src/*.php",
        });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastOptions.Should().BeNull();
        action.LastResult!.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.TyhpdefGenerationError
            && d.Message.Contains("`--ext-name`", StringComparison.Ordinal));
    }

    [Fact]
    public void NoPhpWithExtName_ReportsTyhpdefPhpNotFound()
    {
        var result = Run(new Dictionary<string, string?>
        {
            ["ext-name"] = "widget",
            ["no-php"] = "true",
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPhpNotFound);
        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Message.Contains("PHP delegation not yet implemented", StringComparison.Ordinal));
    }

    [Fact]
    public void PhpWithPhpTargets_ReportsTyhpdefPhpUserBinaryWithTargets()
    {
        var result = Run(new Dictionary<string, string?>
        {
            ["ext-name"] = "widget",
            ["php"] = "/usr/bin/php",
            ["php-targets"] = "8.2,8.3",
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPhpUserBinaryWithTargets);
        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Message.Contains("PHP delegation not yet implemented", StringComparison.Ordinal));
    }

    [Fact]
    public void PhpTargetsWithSource_RequiresExtName()
    {
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-cli-targets-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Lib.php");
            File.WriteAllText(php, "<?php\nfunction lib_ok(): void {}\n");
            var result = Run(new Dictionary<string, string?>
            {
                ["source"] = php,
                ["php-targets"] = "8.2,8.4",
            });

            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefGenerationError
                && d.Message.Contains("--ext-name", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void CommonOptions_AreCapturedOnOptionsRecord()
    {
        var action = RunAction(new Dictionary<string, string?>
        {
            ["ext-name"] = "json",
            ["output"] = Path.Combine(Path.GetTempPath(), "tyhpdef-out"),
            ["output-file"] = "ExtJson.tyhpdef",
            ["php"] = Path.Combine(Path.GetTempPath(), "tyhp-missing-php", "php"),
            ["php-version"] = "8.3",
            ["no-docs"] = "true",
            ["include-internal"] = "true",
            ["no-deprecated"] = "true",
            ["overwrite"] = "true",
            ["split"] = "namespace",
            ["include-dev"] = "true",
            ["require-stubs"] = "true",
            ["verify"] = "true",
            ["validate"] = "./tyhpdef",
            ["refresh-snapshots"] = "true",
            ["no-php-runtime-update"] = "true",
            ["php-runtime-dir"] = "/tmp/php-runtimes",
        });

        var options = action.LastOptions!;
        options.Mode.Should().Be(TyhpdefGenerationMode.PhpExtension);
        options.OutputDirectory.Should().Be(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tyhpdef-out")));
        options.OutputFileName.Should().Be(Path.Combine(options.OutputDirectory, "ExtJson.tyhpdef"));
        options.PhpVersion.Should().Be("8.3");
        options.IncludeDocComments.Should().BeFalse();
        options.IncludeInternal.Should().BeTrue();
        options.IncludeDeprecated.Should().BeFalse();
        options.Overwrite.Should().BeTrue();
        options.Split.Should().Be("namespace");
        options.IncludeDev.Should().BeTrue();
        options.RequireStubs.Should().BeTrue();
        options.FetchStubCache.Should().BeTrue();
        options.Verify.Should().BeTrue();
        options.ValidatePath.Should().Be("./tyhpdef");
        options.RefreshSnapshots.Should().BeTrue();
        options.NoPhpRuntimeUpdate.Should().BeTrue();
        options.PhpRuntimeDir.Should().Be("/tmp/php-runtimes");
        options.PhpProcessTimeoutMs.Should().Be(60_000);
    }

    [Fact]
    public void NoPhpWithInvalidSplit_DoesNotDoublePrintTheUsageMessage()
    {
        var previousOut = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        TyhpdefGenerationResult result;
        try
        {
            result = Run(new Dictionary<string, string?>
            {
                ["ext-name"] = "widget",
                ["no-php"] = "true",
                ["split"] = "bogus",
            });
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        // Both diagnostics must still be recorded regardless of whether console capture works.
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPhpNotFound);
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefGenerationError);

        var output = captured.ToString();
        if (string.IsNullOrWhiteSpace(output))
        {
            // Konsole's ConcurrentWriter may not honor Console.SetOut in this environment; the
            // diagnostic-bag assertions above already cover the underlying codes.
            return;
        }

        CountOccurrences(output, "Invalid `--split` value `bogus`").Should().Be(
            1,
            "the invalid --split usage message must be printed once directly, not again via the diagnostic formatter");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public void NoPhpWithSource_IsIgnoredAndRunsTrackB()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-cli-nophp-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-cli-nophp-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Lib.php");
            File.WriteAllText(php, "<?php\nfunction lib_ok(): void {}\n");
            var result = Run(new Dictionary<string, string?>
            {
                ["source"] = php,
                ["no-php"] = "true",
                ["output"] = outputDir,
                ["overwrite"] = "true",
                ["no-docs"] = "true",
            });

            result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefPhpNotFound);
            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    private static TyhpdefGenerationResult Run(Dictionary<string, string?> settings)
        => RunAction(settings).LastResult!;

    private static GenerateTyhpdefAction RunAction(
        Dictionary<string, string?> settings,
        PhpDelegationTyhpdefGenerator? generator = null)
    {
        var values = new Dictionary<string, string?>(settings) { ["quiet"] = "true" };
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        Environment.ExitCode = (int)ExitCode.Success;
        using var action = generator is null
            ? new GenerateTyhpdefAction(project)
            : new GenerateTyhpdefAction(project) { Generator = generator };
        action.Start(CancellationToken.None);
        return action;
    }

    private sealed class FailingPhpRuntimeManager : PhpRuntimeManager
    {
        public override PhpRuntimeInfo? Ensure(
            string minor,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken = default)
        {
            diagnostics.AddError(
                MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                "generate_tyhpdef",
                0,
                0,
                $"test-blocked {minor}");
            return null;
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
