using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Integration;

[Trait("Category", "Integration")]
public class BuildPipelineIntegrationTests
{
    [Fact]
    public void Build_MultiFileProject_WritesAllOutputs()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithConfigValue("clean", "true")
            .WithTyhpFile("User.tyhp", """
                <?tyhp
                namespace App\Models;
                class User {}
                """)
            .WithTyhpFile("Greeter.tyhp", """
                <?tyhp
                namespace App\Services;
                class Greeter {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty();

        File.Exists(Path.Combine(project.ProjectDirectory, "build", "App", "Models", "User.php")).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "build", "App", "Services", "Greeter.php")).Should().BeTrue();
    }

    [Fact]
    public void Build_StatementFormNamespace_WritesClassToPsr4NamespacePath()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithTyhpFile("Greeter.tyhp", """
                <?tyhp
                namespace App;
                class Greeter {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty();

        var outputFile = Path.Combine(project.ProjectDirectory, "build", "App", "Greeter.php");
        File.Exists(outputFile).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "build", "Greeter.php")).Should().BeFalse();
        File.ReadAllText(outputFile).Should().Contain("namespace App;");
    }

    [Fact]
    public void Build_StrictTypesConfig_IsReflectedInOutput()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/", "strictTypes": false }
                }
                """)
            .WithConfigValue("output:strictTypes", "false")
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                namespace App;
                class Example {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty();

        var output = File.ReadAllText(Path.Combine(project.ProjectDirectory, "build", "App", "Example.php"));
        output.Should().NotContain("declare(strict_types=1);");
    }

    [Fact]
    public void Build_OutputPathConfig_WritesToConfiguredDirectory()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": { "path": "dist/" }
                }
                """)
            .WithConfigValue("output:path", "dist/")
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                namespace App;
                class Example {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty();
        File.Exists(Path.Combine(project.ProjectDirectory, "dist", "App", "Example.php")).Should().BeTrue();
    }
}

[Trait("Category", "Integration")]
public class CompilationServiceIntegrationTests
{
    [Fact]
    public void ParseFiles_MultipleInputs_ReturnsCombinedDiagnostics()
    {
        using var compilationService = new CompilationService();
        var files = TestFileManager
            .GetAllTestDataFiles("ValidTyhp/parser", ".tyhp")
            .Take(5)
            .ToList();

        files.Should().NotBeEmpty();
        var result = compilationService.ParseFiles(
            files,
            new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

        result.ParsedFiles.Should().NotBeNull();
        result.ParsedFiles!.Count.Should().Be(files.Count);
    }
}

[Trait("Category", "Integration")]
public class DiagnosticReportingIntegrationTests
{
    [Fact]
    public void Build_KnownSyntaxError_ReportsFileLocation()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithTyhpFile("Bad.tyhp", """
                <?tyhp
                class Bad {
                    public function run(): void {
                        echo 'unclosed
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().NotBeEmpty();
        result.Diagnostics.Errors.Should().Contain(d =>
            d.FileName.EndsWith("Bad.tyhp", StringComparison.OrdinalIgnoreCase)
            && d.Line > 0);
    }
}
