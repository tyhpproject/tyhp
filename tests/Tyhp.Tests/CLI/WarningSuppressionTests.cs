using Tyhp.CLI;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class WarningSuppressionTests
{
    private const string CleanSource = """
        <?tyhp
        namespace App;

        class Demo {}
        """;

    [Fact]
    public void Lint_SuppressesConfiguredWarningCode()
    {
        using var builder = CreateProject(suppressTyhp8027: false);
        var unsuppressed = new LintAction(builder.BuildProject(), LintAction.CreateFormatter("text", quiet: true))
            .Start(CancellationToken.None);

        unsuppressed.Should().NotBeNull();
        unsuppressed!.Diagnostics.HasErrors.Should().BeFalse();
        unsuppressed.Diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefRuntimePackageNotFound);

        using var suppressedBuilder = CreateProject(suppressTyhp8027: true);
        var suppressed = new LintAction(suppressedBuilder.BuildProject(), LintAction.CreateFormatter("text", quiet: true))
            .Start(CancellationToken.None);

        suppressed.Should().NotBeNull();
        suppressed!.Diagnostics.HasErrors.Should().BeFalse();
        suppressed.Diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefRuntimePackageNotFound);
    }

    [Fact]
    public void Build_SuppressesConfiguredWarningCode()
    {
        using var builder = CreateProject(suppressTyhp8027: true);
        var project = builder.BuildProject();
        var result = new BuildAction(project).Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.HasErrors.Should().BeFalse();
        result.Diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefRuntimePackageNotFound);
    }

    [Fact]
    public void Lint_SuppressedWarningDoesNotFailStrict()
    {
        using var builder = CreateProject(suppressTyhp8027: true)
            .WithConfigValue("strict", "true");
        var project = builder.BuildProject();
        var result = new LintAction(project, LintAction.CreateFormatter("text", quiet: true))
            .Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.HasErrors.Should().BeFalse();
        result.Diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefRuntimePackageNotFound);

        if (!result.Diagnostics.HasWarnings)
        {
            result.GetExitCode(project.Strict).Should().Be(ExitCode.Success);
        }
    }

    private static TestProjectBuilder CreateProject(bool suppressTyhp8027)
    {
        var builder = new TestProjectBuilder()
            .WithDefaultTyhpJson()
            .WithTyhpFile("src/Demo.tyhp", CleanSource)
            .WithConfigValue("no-cache", "true")
            .WithConfigValue("quiet", "true")
            .WithConfigValue("output:phpVersion", "8.2");

        if (suppressTyhp8027)
        {
            builder.WithConfigValue("suppressWarnings:0", "TYHP8027");
        }

        return builder;
    }
}
