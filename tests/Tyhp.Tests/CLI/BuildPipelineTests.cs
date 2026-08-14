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

    private static Project CreateProject(
        string projectPath,
        string outputPath,
        bool clean,
        string includePath = "**/*.tyhp")
    {
        var projectFile = Path.Combine(projectPath, "tyhp.json");
        if (!File.Exists(projectFile))
        {
            File.WriteAllText(projectFile, "{}");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = projectFile,
                ["output:path"] = outputPath,
                ["clean"] = clean.ToString().ToLowerInvariant(),
                ["include:0"] = includePath,
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
