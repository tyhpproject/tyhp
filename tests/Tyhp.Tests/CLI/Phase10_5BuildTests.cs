using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Build")]
public class Phase10_5BuildTests
{
    [Fact]
    public void ParseFiles_BinderOnlyError_AttributesErrorToBindPhase()
    {
        using var compilationService = new CompilationService();
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-phase105-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "BinderError.tyhp");
        File.WriteAllText(filePath, """
            <?tyhp
            function test(UnknownType $missing): void {
            }
            """);

        try
        {
            var result = compilationService.ParseFiles(
                [filePath],
                new CompilationOptions
                {
                    EnableAstCache = false,
                    PhpVersion = "8.4",
                    ProjectPath = tempDir,
                });

            result.ParseErrorCount.Should().Be(0);
            result.BindErrorCount.Should().Be(1);
            result.CheckErrorCount.Should().Be(0);
            result.Diagnostics.ErrorCount.Should().Be(1);
            result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ParseFiles_CheckerOnlyError_AttributesErrorToCheckPhase()
    {
        using var compilationService = new CompilationService();
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-phase105-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "CheckerError.tyhp");
        File.WriteAllText(filePath, """
            <?tyhp
            function demo($value): void {}
            """);

        try
        {
            var result = compilationService.ParseFiles(
                [filePath],
                new CompilationOptions
                {
                    EnableAstCache = false,
                    PhpVersion = "8.4",
                    ProjectPath = tempDir,
                });

            result.ParseErrorCount.Should().Be(0);
            result.BindErrorCount.Should().Be(0);
            result.CheckErrorCount.Should().Be(1);
            result.Diagnostics.ErrorCount.Should().Be(1);
            result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariableTypeRequired);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Build_DeletedOutputWithoutSourceChange_RebuildsInsteadOfSkipping()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-phase105-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var projectFile = Path.Combine(tempDir, "tyhp.json");
        File.WriteAllText(projectFile, """
            {
                "include": ["**/*.tyhp"],
                "output": { "path": "build/" }
            }
            """);
        File.WriteAllText(Path.Combine(tempDir, "App.tyhp"), """
            <?tyhp
            namespace App;
            class Example {}
            """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(projectFile, optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = projectFile,
                    ["clean"] = "true",
                    ["build:dryRun"] = "false",
                })
                .Build();

            var firstBuild = new BuildAction(new Project(configuration)).Start(CancellationToken.None);
            firstBuild.Should().NotBeNull();
            firstBuild!.Diagnostics.Errors.Should().BeEmpty();

            var outputFile = Path.Combine(tempDir, "build", "App", "Example.php");
            File.Exists(outputFile).Should().BeTrue();

            var incrementalConfiguration = new ConfigurationBuilder()
                .AddJsonFile(projectFile, optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = projectFile,
                    ["clean"] = "false",
                    ["build:dryRun"] = "false",
                })
                .Build();

            var unchangedBuild = new BuildAction(new Project(incrementalConfiguration)).Start(CancellationToken.None);
            unchangedBuild.Should().NotBeNull();
            unchangedBuild!.IncrementalBuildSkipped.Should().BeTrue();

            File.Delete(outputFile);

            var rebuildAfterDeletion = new BuildAction(new Project(incrementalConfiguration)).Start(CancellationToken.None);
            rebuildAfterDeletion.Should().NotBeNull();
            rebuildAfterDeletion!.IncrementalBuildSkipped.Should().BeFalse();
            rebuildAfterDeletion.Diagnostics.Errors.Should().BeEmpty();
            File.Exists(outputFile).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
