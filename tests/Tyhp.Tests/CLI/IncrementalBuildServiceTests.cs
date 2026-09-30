using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Build")]
public class IncrementalBuildServiceTests
{
    [Fact]
    public void DetermineChangedFiles_ClassifiesNewChangedRemovedAndUnchanged()
    {
        var tempDir = CreateTempDirectory();
        var unchangedFile = Path.Combine(tempDir, "unchanged.tyhp");
        var changedFile = Path.Combine(tempDir, "changed.tyhp");
        var newFile = Path.Combine(tempDir, "new.tyhp");
        File.WriteAllText(unchangedFile, "unchanged");
        File.WriteAllText(changedFile, "original");
        File.WriteAllText(newFile, "new");

        var service = new IncrementalBuildService();
        var previousState = new IncrementalBuildService.BuildState
        {
            FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [NormalizePath(unchangedFile)] = ComputeHash("unchanged"),
                [NormalizePath(changedFile)] = ComputeHash("original"),
                [NormalizePath(Path.Combine(tempDir, "removed.tyhp"))] = ComputeHash("removed"),
            },
        };

        File.WriteAllText(changedFile, "modified");

        var result = service.DetermineChangedFiles(
            [unchangedFile, changedFile, newFile],
            previousState);

        result.UnchangedFiles.Should().Contain(NormalizePath(unchangedFile));
        result.ChangedFiles.Should().Contain(NormalizePath(changedFile));
        result.NewFiles.Should().Contain(NormalizePath(newFile));
        result.RemovedFiles.Should().Contain(NormalizePath(Path.Combine(tempDir, "removed.tyhp")));
        result.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void IsStateValid_ReturnsFalseWhenCompilerVersionChanges()
    {
        var tempDir = CreateTempDirectory();
        var project = CreateProject(tempDir);
        var service = new IncrementalBuildService();
        var state = new IncrementalBuildService.BuildState
        {
            CompilerVersion = "0.0.0.0",
            ConfigHash = IncrementalBuildService.ComputeConfigHash(project),
        };

        service.IsStateValid(state, project).Should().BeFalse();
    }

    [Fact]
    public void SaveAndLoadBuildState_RoundTripsFileHashes()
    {
        var tempDir = CreateTempDirectory();
        var sourceFile = Path.Combine(tempDir, "App.tyhp");
        File.WriteAllText(sourceFile, "<?tyhp\nclass App {}");
        var cacheDir = Path.Combine(tempDir, ".tyhp-cache");
        var project = CreateProject(tempDir, cacheDir);
        var statePath = IncrementalBuildService.GetBuildStatePath(project);
        Path.GetFileName(statePath).Should().Be(IncrementalBuildService.BuildStateFileName);
        statePath.Should().StartWith(cacheDir + Path.DirectorySeparatorChar);
        var service = new IncrementalBuildService();

        service.SaveBuildState(statePath, [sourceFile], project);
        var loaded = service.LoadBuildState(statePath);

        loaded.Should().NotBeNull();
        loaded!.CompilerVersion.Should().Be(IncrementalBuildService.GetCompilerVersion());
        loaded.ConfigHash.Should().Be(IncrementalBuildService.ComputeConfigHash(project));
        loaded.FileHashes.Should().ContainKey(NormalizePath(sourceFile));
        service.IsStateValid(loaded, project).Should().BeTrue();
    }

    [Fact]
    public void SaveAndLoadBuildState_RoundTripsOutputFilePaths()
    {
        var tempDir = CreateTempDirectory();
        var sourceFile = Path.Combine(tempDir, "App.tyhp");
        var outputFile = Path.Combine(tempDir, "build", "App.php");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(sourceFile, "<?tyhp\nclass App {}");
        File.WriteAllText(outputFile, "<?php\nclass App {}");
        var cacheDir = Path.Combine(tempDir, ".tyhp-cache");
        var project = CreateProject(tempDir, cacheDir);
        var statePath = IncrementalBuildService.GetBuildStatePath(project);
        statePath.Should().StartWith(cacheDir + Path.DirectorySeparatorChar);
        var outputStatePath = Path.Combine(tempDir, "build", IncrementalBuildService.BuildStateFileName);
        var service = new IncrementalBuildService();

        service.SaveBuildState(statePath, [sourceFile], project, [outputFile]);
        File.Exists(outputStatePath).Should().BeFalse();
        var loaded = service.LoadBuildState(statePath);

        loaded.Should().NotBeNull();
        loaded!.OutputFilePaths.Should().ContainSingle()
            .Which.Should().Be(NormalizePath(outputFile));
    }

    [Fact]
    public void AllOutputFilesExist_ReturnsTrueWhenAllRecordedOutputsExist()
    {
        var tempDir = CreateTempDirectory();
        var outputFile = Path.Combine(tempDir, "build", "App.php");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllText(outputFile, "<?php");
        var service = new IncrementalBuildService();
        var state = new IncrementalBuildService.BuildState
        {
            OutputFilePaths = [NormalizePath(outputFile)],
        };

        service.AllOutputFilesExist(state).Should().BeTrue();
    }

    [Fact]
    public void AllOutputFilesExist_ReturnsFalseWhenRecordedOutputIsMissing()
    {
        var tempDir = CreateTempDirectory();
        var missingOutput = Path.Combine(tempDir, "build", "App.php");
        var service = new IncrementalBuildService();
        var state = new IncrementalBuildService.BuildState
        {
            OutputFilePaths = [NormalizePath(missingOutput)],
        };

        service.AllOutputFilesExist(state).Should().BeFalse();
    }

    [Fact]
    public void AllOutputFilesExist_ReturnsTrueForLegacyStateWithoutOutputPaths()
    {
        var service = new IncrementalBuildService();
        var state = new IncrementalBuildService.BuildState
        {
            FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        service.AllOutputFilesExist(state).Should().BeTrue();
    }

    [Fact]
    public void DeleteBuildState_RemovesStateFile()
    {
        var tempDir = CreateTempDirectory();
        var cacheDir = Path.Combine(tempDir, ".tyhp-cache");
        var project = CreateProject(tempDir, cacheDir);
        var statePath = IncrementalBuildService.GetBuildStatePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, "{}");

        IncrementalBuildService.DeleteBuildState(statePath);

        File.Exists(statePath).Should().BeFalse();
    }

    [Fact]
    public void GetBuildStatePath_SeparatesProjectsThatShareACacheDirectory()
    {
        var root = CreateTempDirectory();
        var cacheDir = Path.Combine(root, "cache");
        var projectA = CreateProject(Path.Combine(root, "a"), cacheDir);
        var projectB = CreateProject(Path.Combine(root, "b"), cacheDir);

        var pathA = IncrementalBuildService.GetBuildStatePath(projectA);
        var pathB = IncrementalBuildService.GetBuildStatePath(projectB);

        pathA.Should().NotBe(pathB);
        Path.GetFileName(pathA).Should().Be(IncrementalBuildService.BuildStateFileName);
        Path.GetFileName(pathB).Should().Be(IncrementalBuildService.BuildStateFileName);
        pathA.Should().StartWith(cacheDir + Path.DirectorySeparatorChar);
        pathB.Should().StartWith(cacheDir + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void GetBuildStatePath_UsesAstCacheRootWhenCacheDirUnset()
    {
        var tempDir = CreateTempDirectory();
        var project = CreateProject(tempDir);
        var statePath = IncrementalBuildService.GetBuildStatePath(project);
        var cacheRoot = AstCacheService.ResolveCacheRootDirectory(null);
        var outputDir = BuildOutputCleaner.ResolveOutputDirectory(
            PathCanonicalizer.GetCanonicalFullPath(project.GetProjectPath()),
            project.Output.Path);

        statePath.Should().StartWith(cacheRoot + Path.DirectorySeparatorChar);
        Path.GetFileName(statePath).Should().Be(IncrementalBuildService.BuildStateFileName);
        Path.GetDirectoryName(statePath).Should().NotBe(outputDir);
    }

    private static Project CreateProject(string projectPath, string? cacheDir = null)
    {
        Directory.CreateDirectory(projectPath);
        var projectFile = Path.Combine(projectPath, "tyhp.json");
        File.WriteAllText(projectFile, """
            {
                "include": ["**/*.tyhp"],
                "output": { "path": "build/" }
            }
            """);

        var values = new Dictionary<string, string?>
        {
            ["*project_file_path"] = projectFile,
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

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-incremental-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static string NormalizePath(string path)
        => PathCanonicalizer.GetCanonicalFullPath(path).Replace('\\', '/');

    private static string ComputeHash(string content)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    [Fact]
    public void DetermineChangedFiles_TreatsSymlinkAndPhysicalSpellingsAsSameFile()
    {
        using var layout = SymlinkProjectLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var service = new IncrementalBuildService();
        var previousState = new IncrementalBuildService.BuildState
        {
            FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [NormalizePath(layout.RealSourceFile)] = ComputeHash("<?tyhp\n"),
            },
        };

        var result = service.DetermineChangedFiles([layout.LinkSourceFile], previousState);

        result.UnchangedFiles.Should().Contain(NormalizePath(layout.RealSourceFile));
        result.NewFiles.Should().BeEmpty();
        result.ChangedFiles.Should().BeEmpty();
        result.HasChanges.Should().BeFalse();
    }

    private sealed class SymlinkProjectLayout : IDisposable
    {
        private SymlinkProjectLayout(string realRoot, string realSourceFile, string linkSourceFile)
        {
            RealRoot = realRoot;
            RealSourceFile = realSourceFile;
            LinkSourceFile = linkSourceFile;
        }

        public string RealRoot { get; }
        public string RealSourceFile { get; }
        public string LinkSourceFile { get; }

        public static SymlinkProjectLayout? TryCreate()
        {
            var id = Guid.NewGuid().ToString("N");
            var parent = Path.Combine(Path.GetTempPath(), "tyhp-symlink-incremental-" + id);
            var realRoot = Path.Combine(parent, "real");
            var linkRoot = Path.Combine(parent, "link");
            Directory.CreateDirectory(realRoot);
            var realSourceFile = Path.Combine(realRoot, "App.tyhp");
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

            return new SymlinkProjectLayout(
                realRoot,
                realSourceFile,
                Path.Combine(linkRoot, "App.tyhp"));
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
public class TyhpLibDistributionServiceTests
{
    [Fact]
    public void DetermineRequiredPackages_PrefersEmitContextPackages()
    {
        var emitContext = EmitContext.Create(null, new Tyhp.Domain.Diagnostics.DiagnosticBag());
        emitContext.RequirePackage("tyhp/async");
        emitContext.RequirePackage("tyhp/core");

        var packages = TyhpLibDistributionService.DetermineRequiredPackages([], emitContext);

        packages.Should().BeEquivalentTo(["tyhp/async", "tyhp/core", "tyhpdef/php"]);
    }

    [Fact]
    public void ValidateInteropContractVersions_AcceptsStampedRuntimePackages()
    {
        var diagnostics = new Tyhp.Domain.Diagnostics.DiagnosticBag();
        var service = new TyhpLibDistributionService(diagnostics);

        service.ValidateInteropContractVersions(["tyhp/core", "tyhp/async", "tyhp/decimal", "tyhp/lambda"]);

        diagnostics.Errors.Should().BeEmpty();
    }
}
