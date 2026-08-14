using Tyhp.CLI;
using Tyhp.Domain.Enums;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

/// <summary>
/// Regression for Story 13 Phase 2 §2: <c>--strict</c> must promote warnings-only runs to
/// <see cref="ExitCode.CompileError"/> for both lint and build.
/// </summary>
[Trait("Category", "CLI")]
public class StrictModeExitCodeTests
{
    // Minimal clean source. Test projects typically also emit Tyhpdef* package-not-found
    // warnings (no Composer packages under the temp root), which is enough for warnings-only.
    private const string CleanSource = """
        <?tyhp
        namespace App;

        class Demo {}
        """;

    [Fact]
    public void Lint_WarningsOnly_WithoutStrict_ExitsCompileWarning()
    {
        using var builder = CreateProject(strict: false);
        var project = builder.BuildProject();
        var result = new LintAction(project, LintAction.CreateFormatter("text", quiet: true))
            .Start(CancellationToken.None);

        AssertWarningsOnly(result);
        result!.GetExitCode(project.Strict).Should().Be(ExitCode.CompileWarning);
    }

    [Fact]
    public void Lint_WarningsOnly_WithStrict_ExitsCompileError()
    {
        using var builder = CreateProject(strict: true);
        var project = builder.BuildProject();
        var result = new LintAction(project, LintAction.CreateFormatter("text", quiet: true))
            .Start(CancellationToken.None);

        AssertWarningsOnly(result);
        project.Strict.Should().BeTrue();
        result!.GetExitCode(project.Strict).Should().Be(ExitCode.CompileError);
    }

    [Fact]
    public void Build_WarningsOnly_WithoutStrict_ExitsCompileWarning()
    {
        using var builder = CreateProject(strict: false);
        var project = builder.BuildProject();
        var result = new BuildAction(project).Start(CancellationToken.None);

        AssertWarningsOnly(result);
        result!.GetExitCode(project.Strict).Should().Be(ExitCode.CompileWarning);
    }

    [Fact]
    public void Build_WarningsOnly_WithStrict_ExitsCompileError()
    {
        using var builder = CreateProject(strict: true);
        var project = builder.BuildProject();
        var result = new BuildAction(project).Start(CancellationToken.None);

        AssertWarningsOnly(result);
        project.Strict.Should().BeTrue();
        result!.GetExitCode(project.Strict).Should().Be(ExitCode.CompileError);
    }

    private static void AssertWarningsOnly(Tyhp.Domain.Diagnostics.CompilationResult? result)
    {
        result.Should().NotBeNull();
        result!.Diagnostics.HasErrors.Should().BeFalse(
            "unexpected errors: {0}",
            string.Join("; ", result.Diagnostics.Errors.Select(d => $"{d.Code}:{d.Message}")));
        result.Diagnostics.HasWarnings.Should().BeTrue(
            "expected warnings (e.g. missing runtime tyhpdefs under the temp project)");
    }

    private static TestProjectBuilder CreateProject(bool strict)
    {
        return new TestProjectBuilder()
            .WithDefaultTyhpJson()
            .WithTyhpFile("src/Demo.tyhp", CleanSource)
            .WithConfigValue("no-cache", "true")
            .WithConfigValue("quiet", "true")
            .WithConfigValue("strict", strict ? "true" : "false");
    }
}
