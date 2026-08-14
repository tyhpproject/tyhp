using System.Text.Json;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

/// <summary>
/// Regression for Story 13 Phase 4 §1 (hybrid): config warnings must not corrupt machine-readable
/// stdout. Lint/build fold them into <see cref="DiagnosticBag"/>; <c>version --json</c> uses stderr.
/// </summary>
[Trait("Category", "CLI")]
public class ConfigWarningStdoutHygieneTests
{
    private const string CleanSource = """
        <?tyhp
        namespace App;

        class Demo {}
        """;

    [Fact]
    public void Lint_JsonFormat_WithInvalidProjectType_StdoutIsParseableJson_AndIncludesConfigWarning()
    {
        using var builder = CreateWarnProject()
            .WithConfigValue("format", "json");
        var project = builder.BuildProject();
        var formatterOut = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(formatterOut, prettyPrint: true);

        var previousOut = Console.Out;
        var consoleOut = new StringWriter();
        Console.SetOut(consoleOut);
        try
        {
            var result = new LintAction(project, formatter).Start(CancellationToken.None);

            result.Should().NotBeNull();
            result!.Diagnostics.Warnings.Should().Contain(
                w => w.Code == MessageCode.ConfigInvalidProjectType,
                "config warning TYHP6008 must be folded into the lint DiagnosticBag");

            var json = formatterOut.ToString();
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("diagnostics", out var diagnostics).Should().BeTrue();
            diagnostics.EnumerateArray().Any(d =>
                d.TryGetProperty("code", out var code)
                && code.GetString() == "TYHP6008").Should().BeTrue();

            consoleOut.ToString().Should().NotContain(
                "TYHP6008",
                "config warnings must not be written to stdout for --format=json");
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    [Fact]
    public void Lint_SarifFormat_WithInvalidProjectType_StdoutIsParseableJson_AndIncludesConfigWarning()
    {
        using var builder = CreateWarnProject()
            .WithConfigValue("format", "sarif");
        var project = builder.BuildProject();
        var formatterOut = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(formatterOut, prettyPrint: true);

        var previousOut = Console.Out;
        var consoleOut = new StringWriter();
        Console.SetOut(consoleOut);
        try
        {
            var result = new LintAction(project, formatter).Start(CancellationToken.None);

            result.Should().NotBeNull();
            result!.Diagnostics.Warnings.Should().Contain(
                w => w.Code == MessageCode.ConfigInvalidProjectType);

            using var doc = JsonDocument.Parse(formatterOut.ToString());
            doc.RootElement.TryGetProperty("runs", out _).Should().BeTrue(
                "SARIF document must remain parseable when config warns");

            consoleOut.ToString().Should().NotContain("TYHP6008");
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    [Fact]
    public void VersionJson_WithInvalidProjectType_StdoutIsParseableJson_ConfigWarningOnStderr()
    {
        using var builder = CreateWarnProject()
            .WithConfigValue("json", "true");
        var project = builder.BuildProject();

        var previousOut = Console.Out;
        var previousErr = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            // HostedService flushes pending config warnings to stderr for version --json.
            project.EmitPendingConfigWarningsToStderr();
            new VersionAction(project).Start(CancellationToken.None);

            using var doc = JsonDocument.Parse(stdout.ToString());
            doc.RootElement.TryGetProperty("tyhp", out _).Should().BeTrue();

            stdout.ToString().Should().NotContain("TYHP6008");
            stderr.ToString().Should().Contain("TYHP6008");
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousErr);
        }
    }

    [Fact]
    public void Build_WithInvalidProjectType_AndParseableSources_IncludesConfigWarningInFinalBag()
    {
        // BuildAction.Start() replaces `result` with a fresh CompilationResult once
        // CompilationService.ParseFiles() runs (the normal path whenever there are source
        // files). Pending config warnings must survive that reassignment, not just the
        // early-return paths (invalid output path, no source files, etc.).
        using var builder = CreateWarnProject();
        var project = builder.BuildProject();

        var result = new BuildAction(project).Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.Warnings.Should().Contain(
            w => w.Code == MessageCode.ConfigInvalidProjectType,
            "config warning TYHP6008 must survive the CompilationResult reassignment after parsing");
    }

    private static TestProjectBuilder CreateWarnProject()
    {
        return new TestProjectBuilder()
            .WithTyhpJson("""
                {
                    "type": "bogus",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("src/Demo.tyhp", CleanSource)
            .WithConfigValue("no-cache", "true")
            .WithConfigValue("quiet", "false");
    }
}
