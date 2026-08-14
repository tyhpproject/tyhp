using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.EndToEnd;

[Trait("Category", "EndToEnd")]
[Trait("Category", "Emitter")]
public class SnapshotTests
{
    public static IEnumerable<object[]> EmitterFixtureFiles()
        => TestFileManager.GetAllTestDataFiles("ValidTyhp/emitter", ".tyhp")
            .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(EmitterFixtureFiles))]
    public void Emit_EmitterFixtures_MatchSnapshots(string inputPath)
    {
        var result = Compile(inputPath);
        result.Diagnostics.Errors.Should().BeEmpty($"parse/bind/check should succeed for {inputPath}");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        outputFiles.Should().NotBeEmpty();

        var php = string.Join('\n', outputFiles
            .OrderBy(file => file.OutputFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.GeneratedContent ?? string.Empty));

        var snapshotName = Path.GetFileNameWithoutExtension(inputPath) + ".php";
        SnapshotManager.AssertMatchesSnapshot(php, snapshotName, "Emitter");
    }

    private static CompilationResult Compile(string filePath)
    {
        using var compilationService = new CompilationService();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            PhpVersion = "8.4",
            ProjectPath = TestFileManager.GetRepoRoot(),
            TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
        };

        return compilationService.ParseFiles([filePath], options);
    }
}

[Trait("Category", "EndToEnd")]
public class FullPipelineTests
{
    [Fact]
    public void Build_MinimalProject_WritesPhpOutput()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithTyhpFile("src/App.tyhp", """
                <?tyhp
                namespace App;
                class Example {
                    public function run(): void {}
                }
                """);

        var result = project.RunBuild();

        result.Diagnostics.Errors.Should().BeEmpty();
        var outputFile = Path.Combine(project.ProjectDirectory, "build", "App", "Example.php");
        File.Exists(outputFile).Should().BeTrue();
        File.ReadAllText(outputFile).Should().Contain("class Example");
    }

    [Fact]
    public void Build_InvalidProject_ReturnsCompileErrors()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithTyhpFile("broken.tyhp", """
                <?tyhp
                class Broken {
                    public function run(): void {
                        echo 'unclosed
                """);

        var result = project.RunBuild();

        result.Diagnostics.HasErrors.Should().BeTrue();
        result.GetExitCode().Should().Be(ExitCode.CompileError);
    }
}

[Trait("Category", "EndToEnd")]
public class PhpOutputValidationTests
{
    [Theory]
    [MemberData(nameof(SnapshotTests.EmitterFixtureFiles), MemberType = typeof(SnapshotTests))]
    public void Emit_EmitterFixtures_ProduceValidPhpSyntax(string inputPath)
    {
        if (!PhpToolchain.IsAvailable())
        {
            return;
        }

        using var compilationService = new CompilationService();
        var result = compilationService.ParseFiles(
            [inputPath],
            new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

        result.Diagnostics.Errors.Should().BeEmpty();
        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        outputFiles.Should().NotBeEmpty();

        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-php-lint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var outputFile in outputFiles)
            {
                var path = Path.Combine(tempDir, Path.GetFileName(outputFile.OutputFilePath));
                File.WriteAllText(path, outputFile.GeneratedContent ?? string.Empty);
                var lintResult = PhpToolchain.RunPhpLint(path);
                lintResult.ExitCode.Should().Be(0, $"php -l failed for {path}:\n{lintResult.CombinedOutput}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
