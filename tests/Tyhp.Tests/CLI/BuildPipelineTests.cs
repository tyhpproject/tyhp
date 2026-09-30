using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Build")]
public class BuildOutputCleanerTests
{
    [Fact]
    public void TryClean_RefusesProjectRoot()
    {
        var tempDir = CreateTempDirectory();
        var project = CreateProject(tempDir, outputPath: ".", clean: true);

        var diagnostics = new DiagnosticBag();
        var cleaned = BuildOutputCleaner.TryClean(project, diagnostics);

        cleaned.Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildCleanFailed);
    }

    [Fact]
    public void TryClean_DeletesGeneratedPhpFiles()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "App.php"), "<?php");
        File.WriteAllText(Path.Combine(outputDir, "README.md"), "keep");

        var project = CreateProject(tempDir, outputPath: "build", clean: true);
        var diagnostics = new DiagnosticBag();

        BuildOutputCleaner.TryClean(project, diagnostics).Should().BeTrue();
        Directory.Exists(outputDir).Should().BeTrue();
        Directory.GetFiles(outputDir, "*.php", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(Path.Combine(outputDir, "README.md")).Should().BeTrue();
    }

    [Fact]
    public void TryClean_DeletesBuildStateFromCacheDirectoryNotOutput()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(tempDir, "build");
        var cacheDir = Path.Combine(tempDir, ".tyhp-cache");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "App.php"), "<?php");
        File.WriteAllText(Path.Combine(outputDir, IncrementalBuildService.BuildStateFileName), "stale-output-copy");

        var project = CreateProject(tempDir, outputPath: "build", clean: true, cacheDir: cacheDir);
        var statePath = IncrementalBuildService.GetBuildStatePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, "{}");

        var diagnostics = new DiagnosticBag();
        BuildOutputCleaner.TryClean(project, diagnostics).Should().BeTrue();

        File.Exists(statePath).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, IncrementalBuildService.BuildStateFileName)).Should().BeTrue();
        Directory.GetFiles(outputDir, "*.php", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void TryClean_DeletesBuildStateWhenOutputDirectoryIsMissing()
    {
        var tempDir = CreateTempDirectory();
        var cacheDir = Path.Combine(tempDir, ".tyhp-cache");
        var project = CreateProject(tempDir, outputPath: "build", clean: true, cacheDir: cacheDir);
        var statePath = IncrementalBuildService.GetBuildStatePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, "{}");

        var diagnostics = new DiagnosticBag();
        BuildOutputCleaner.TryClean(project, diagnostics).Should().BeTrue();

        Directory.Exists(Path.Combine(tempDir, "build")).Should().BeFalse();
        File.Exists(statePath).Should().BeFalse();
    }

    [Fact]
    public void TryClean_RefusesAbsoluteOutputThatOverlapsSourceViaSymlinkSpelling()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        // Physical project root + absolute output under the symlink spelling of the same tree's
        // source include. Without PathCanonicalizer the overlap check misses and would clean src/.
        var project = CreateProject(
            layout.RealRoot,
            outputPath: Path.Combine(layout.LinkRoot, "src"),
            clean: true,
            includePath: "src");
        var diagnostics = new DiagnosticBag();

        BuildOutputCleaner.TryClean(project, diagnostics).Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildCleanFailed);
        File.Exists(layout.RealSourceFile).Should().BeTrue();
    }

    [Fact]
    public void TryCleanPublish_RefusesProjectRoot()
    {
        var tempDir = CreateTempDirectory();
        var project = CreatePublishProject(tempDir, publishPath: ".", publishClean: true);

        var diagnostics = new DiagnosticBag();
        BuildOutputCleaner.TryCleanPublish(project, diagnostics).Should().BeFalse();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildCleanFailed);
    }

    [Fact]
    public void TryCleanPublish_DeletesPublishDirectory()
    {
        var tempDir = CreateTempDirectory();
        var publishDir = Path.Combine(tempDir, "publish");
        Directory.CreateDirectory(publishDir);
        File.WriteAllText(Path.Combine(publishDir, "stale.txt"), "gone");

        var project = CreatePublishProject(tempDir, publishPath: "publish", publishClean: true);
        var diagnostics = new DiagnosticBag();

        BuildOutputCleaner.TryCleanPublish(project, diagnostics).Should().BeTrue();
        Directory.Exists(publishDir).Should().BeTrue();
        Directory.GetFileSystemEntries(publishDir).Should().BeEmpty();
    }

    private static Project CreateProject(
        string projectPath,
        string outputPath,
        bool clean,
        string includePath = "**/*.tyhp",
        string? cacheDir = null)
    {
        var projectFile = Path.Combine(projectPath, "tyhp.json");
        if (!File.Exists(projectFile))
        {
            File.WriteAllText(projectFile, "{}");
        }

        var values = new Dictionary<string, string?>
        {
            ["*project_file_path"] = projectFile,
            ["output:path"] = outputPath,
            ["clean"] = clean.ToString().ToLowerInvariant(),
            ["include:0"] = includePath,
        };
        if (cacheDir != null)
        {
            values["cache-dir"] = cacheDir;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new Project(configuration);
    }

    private static Project CreatePublishProject(string projectPath, string publishPath, bool publishClean)
    {
        var projectFile = Path.Combine(projectPath, "tyhp.json");
        if (!File.Exists(projectFile))
        {
            File.WriteAllText(projectFile, "{}");
        }

        Directory.CreateDirectory(Path.Combine(projectPath, "src"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = projectFile,
                ["output:path"] = "src",
                ["output:publishPath"] = publishPath,
                ["output:publishClean"] = publishClean.ToString().ToLowerInvariant(),
                ["include:0"] = "src/**/*.tyhp",
            })
            .Build();

        return new Project(configuration);
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-build-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private sealed class SymlinkProjectLayout : IDisposable
    {
        private SymlinkProjectLayout(string realRoot, string linkRoot, string realSourceFile)
        {
            RealRoot = realRoot;
            LinkRoot = linkRoot;
            RealSourceFile = realSourceFile;
        }

        public string RealRoot { get; }
        public string LinkRoot { get; }
        public string RealSourceFile { get; }

        public static SymlinkProjectLayout? TryCreate()
        {
            var id = Guid.NewGuid().ToString("N");
            var parent = Path.Combine(Path.GetTempPath(), "tyhp-symlink-clean-" + id);
            var realRoot = Path.Combine(parent, "real");
            var linkRoot = Path.Combine(parent, "link");
            var srcDir = Path.Combine(realRoot, "src");

            Directory.CreateDirectory(srcDir);
            var realSourceFile = Path.Combine(srcDir, "index.tyhp");
            File.WriteAllText(Path.Combine(realRoot, "tyhp.json"), """{"include":["src"]}""");
            File.WriteAllText(realSourceFile, "<?tyhp\n");

            try
            {
                Directory.CreateSymbolicLink(linkRoot, realRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
                return null;
            }

            if (!Directory.Exists(linkRoot))
            {
                try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
                return null;
            }

            return new SymlinkProjectLayout(realRoot, linkRoot, realSourceFile);
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(RealRoot);
            if (String.IsNullOrEmpty(parent))
            {
                return;
            }

            try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
        }
    }
}

[Trait("Category", "Build")]
public class BuildEntryPointValidatorTests
{
    [Fact]
    public void ValidateLibraryProject_ReportsEntrypointFiles()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $value = 1;
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<Tyhp.TyhpLang.Ast.SrcFileAst>().Subject;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = "library",
            })
            .Build();

        var project = new Project(configuration);
        var diagnostics = new DiagnosticBag();

        BuildEntryPointValidator.ValidateLibraryProject(project, [srcFile], diagnostics);

        diagnostics.HasErrors.Should().BeTrue();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.TyhpdefLibraryEntrypointDetected);
    }

    [Fact]
    public void ValidateLibraryProject_AllowsDeclarationOnlyFiles()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Example {
                public int $value = 1;
            }
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<Tyhp.TyhpLang.Ast.SrcFileAst>().Subject;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = "library",
            })
            .Build();

        var project = new Project(configuration);
        var diagnostics = new DiagnosticBag();

        BuildEntryPointValidator.ValidateLibraryProject(project, [srcFile], diagnostics);

        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ValidateLibraryProject_AllowsGlobalUseExtension()
    {
        ValidateLibrary("""
            <?tyhp
            namespace Lib;
            global use extension \Lib\Ops;
            extension Ops extends string {
                fn ident(): string => $this;
            }
            """).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ValidateLibraryProject_AllowsDeclarePhpExtensionBlocks()
    {
        ValidateLibrary("""
            <?tyhp
            namespace Lib;
            global use extension \Lib\Ops;
            declare(php=">=8.2") {
                extension Ops extends string {
                    function ident(): string {
                        return $this;
                    }
                }
            }
            """).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ValidateLibraryProject_Reports7505_ForExecutableCodeInsideDeclarePhp()
    {
        var diagnostics = ValidateLibrary("""
            <?tyhp
            namespace Lib;
            declare(php=">=8.2") {
                $x = 1;
            }
            """);

        diagnostics.HasErrors.Should().BeTrue();
        diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.TyhpdefLibraryEntrypointDetected);
    }

    [Fact]
    public void ValidateLibraryProject_SkipsApplicationProjects()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $value = 1;
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<Tyhp.TyhpLang.Ast.SrcFileAst>().Subject;
        var project = new Project(new ConfigurationBuilder().Build());
        var diagnostics = new DiagnosticBag();

        BuildEntryPointValidator.ValidateLibraryProject(project, [srcFile], diagnostics);

        diagnostics.HasErrors.Should().BeFalse();
    }

    private static DiagnosticBag ValidateLibrary(string content)
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(content);
        parseResult.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", parseResult.Diagnostics.Errors.Select(e => e.Message)));
        var srcFile = parseResult.Ast.Should().BeAssignableTo<Tyhp.TyhpLang.Ast.SrcFileAst>().Subject;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = "library",
            })
            .Build();

        var diagnostics = new DiagnosticBag();
        BuildEntryPointValidator.ValidateLibraryProject(new Project(configuration), [srcFile], diagnostics);
        return diagnostics;
    }
}

[Trait("Category", "Build")]
public class EmitConfigProjectTests
{
    [Fact]
    public void EmitConfig_UsesProjectOutputAndBuildSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:path"] = "dist/",
                ["output:namespacePrefix"] = "Vendor",
                ["output:strictTypes"] = "false",
                ["output:comments"] = "false",
                ["output:phpVersion"] = "8.3",
                ["build:entryPointAutoloader:composer"] = "vendor/autoload.php",
            })
            .Build();

        var project = new Project(configuration);
        var config = new EmitConfig(project);

        config.OutputPath.Should().Be("dist/");
        config.PublishPath.Should().Be(".");
        config.NamespacePrefix.Should().Be("Vendor");
        config.StrictTypes.Should().BeFalse();
        config.IncludeComments.Should().BeFalse();
        config.TargetPhpVersion.Should().Be("8.3");
        config.EntryPointAutoloader.Should().Be("vendor/autoload.php");
        config.SourceRoot.Should().Be(project.GetProjectPath());
    }

    [Fact]
    public void EmitConfig_DefaultsEntryPointAutoloaderToComposerVendorPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:path"] = "build/",
            })
            .Build();

        var config = new EmitConfig(new Project(configuration));

        config.EntryPointAutoloader.Should().Be(EmitConfig.DefaultComposerAutoloaderPath);
    }

    [Fact]
    public void EmitConfig_EmptyComposerAutoloader_DisablesInjection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:path"] = "build/",
                ["build:entryPointAutoloader:composer"] = "",
            })
            .Build();

        var config = new EmitConfig(new Project(configuration));

        config.EntryPointAutoloader.Should().BeNull();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("NONE")]
    [InlineData(" none ")]
    public void EmitConfig_NoneComposerAutoloader_DisablesInjection(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:path"] = "build/",
                ["build:entryPointAutoloader:composer"] = value,
            })
            .Build();

        var config = new EmitConfig(new Project(configuration));

        config.EntryPointAutoloader.Should().BeNull();
    }
}
