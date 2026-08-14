using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Diagnostics;

// AstCacheService and Config.Project.Singleton are process-global mutable state. Other tests that
// construct a Project (via CompilationService / TestProjectBuilder) swap the singleton, which would
// change the relative cache key and on-disk cache root between AddOrUpdate and Get. Run
// cache-mutating tests in their own non-parallel collection, and give disk round-trips an isolated
// cache-dir under a dedicated project so Clear()/Flush never touch the shared LocalAppData cache.
[CollectionDefinition("AstCache", DisableParallelization = true)]
public class AstCacheCollection { }

[Trait("Category", "Build")]
[Collection("AstCache")]
public class AstCacheRoundTripTests
{
    private const string DuplicateClassSource = """
        <?tyhp
        class Foo {
            public int $a = 1;
        }

        class Foo {
            public int $b = 2;
        }
        """;

    [Fact]
    public void Serialize_Deserialize_PreservesDuplicateTopLevelDeclarations()
    {
        var src = ParseSource(DuplicateClassSource, out var cleanup);
        try
        {
            CountFoo(src).Should().Be(2);

            var bytes = ((Base2Ast)src).Serialize();
            var round = Base2Ast.Deserialize<SrcFileAst>(bytes, skipChildrenFlagsAndAttributes: false);

            CountFoo(round).Should().Be(2, "duplicate-named declarations must survive a serialize/deserialize round-trip");
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void DiskCacheReload_PreservesAllTopLevelDeclarations()
    {
        using var scope = IsolatedAstCacheScope.Create(DuplicateClassSource);
        try
        {
            var src = ParseFile(scope.SourceFilePath);
            var hash = new string(src.FileHash.Select(b => (char)b).ToArray());

            // Simulate a fresh process: persist to disk, drop the in-memory cache, then reload from disk.
            AstCacheService.Clear();
            AstCacheService.AddOrUpdate(src);
            AstCacheService.FlushMemory();
            AstCacheService.ClearMemory();

            var fromDisk = AstCacheService.Get(scope.SourceFilePath, hash);

            fromDisk.Should().NotBeNull("a cache hit must return the cached AST");
            CountFoo(fromDisk!).Should().Be(2, "a cross-process cache hit must not drop top-level declarations");
        }
        finally
        {
            AstCacheService.Clear();
        }
    }

    [Fact]
    public void DiskCacheReload_SurvivesCwdDifferingFromProjectRoot()
    {
        // Full-suite order flake: Project.Singleton points at a temp project while CWD is the
        // repo (or any other directory). GetRelativePath must not re-resolve already-relative
        // cache keys against CWD, or AddOrUpdate and Get disagree and the disk hit is null.
        var previousCwd = Directory.GetCurrentDirectory();
        using var scope = IsolatedAstCacheScope.Create(DuplicateClassSource);
        try
        {
            Directory.SetCurrentDirectory(TestFileManager.GetRepoRoot());
            Path.GetFullPath(Directory.GetCurrentDirectory()).Should().NotBe(
                Path.GetFullPath(Path.GetDirectoryName(scope.SourceFilePath)!),
                "precondition: CWD must differ from the isolated project root");

            var src = ParseFile(scope.SourceFilePath);
            var hash = new string(src.FileHash.Select(b => (char)b).ToArray());

            // FileName is already project-relative (e.g. "test.tyhp"); double-applying
            // GetRelativePath used to break when CWD ≠ project root.
            src.FileName.Should().Be("test.tyhp");

            AstCacheService.Clear();
            AstCacheService.AddOrUpdate(src);
            AstCacheService.FlushMemory();
            AstCacheService.ClearMemory();

            var fromDisk = AstCacheService.Get(scope.SourceFilePath, hash);
            fromDisk.Should().NotBeNull(
                "cache keying must stay stable when CWD differs from Project.Singleton's project path");
            CountFoo(fromDisk!).Should().Be(2);
        }
        finally
        {
            try { AstCacheService.Clear(); } catch { /* best effort */ }
            Directory.SetCurrentDirectory(previousCwd);
        }
    }

    [Fact]
    public void GetRelativePath_CanonicalizesAbsolutePathThroughProjectSymlink()
    {
        using var layout = SymlinkAstCacheLayout.TryCreate();
        if (layout == null)
        {
            return;
        }

        var previous = Project.Singleton;
        try
        {
            _ = new Project(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = layout.RealProjectFile,
                    ["include:0"] = "**/*.tyhp",
                })
                .Build());

            var relative = AstCacheService.GetRelativePath(layout.LinkSourceFile);
            relative.Replace('\\', '/').Should().Be("src/index.tyhp");
        }
        finally
        {
            Project.Singleton = previous;
        }
    }

    [Fact]
    public void TyhpdefParseContent_SecondCall_HitsAstCacheAndReturnsFreshTree()
    {
        // Use a real package tyhpdef so the path is Absolute/GetRelativePath-friendly (unlike
        // synthetic <tyhpdef:embedded:...> names that depend on CWD).
        var tyhpdefPath = Path.Combine(
            TestFileManager.GetRepoRoot(),
            "runtime",
            "php-extensions",
            "php8.2.9",
            "ExtJson.tyhpdef");
        File.Exists(tyhpdefPath).Should().BeTrue();

        var content = File.ReadAllText(tyhpdefPath);
        var diagnostics = new Tyhp.Domain.Diagnostics.DiagnosticBag();

        try
        {
            AstCacheService.ClearMemory(tyhpdefPath);

            var first = Tyhp.TyhpLang.Binder.BuiltIn.Tyhpdef.ParseContent(
                content,
                tyhpdefPath,
                Tyhp.TyhpLang.Enum.ParseMode.Tyhpdef,
                diagnostics);
            first.Should().NotBeNull();

            // Clear only nothing — in-memory entry from AddOrUpdate should satisfy the second Get.
            var second = Tyhp.TyhpLang.Binder.BuiltIn.Tyhpdef.ParseContent(
                content,
                tyhpdefPath,
                Tyhp.TyhpLang.Enum.ParseMode.Tyhpdef,
                diagnostics);
            second.Should().NotBeNull();

            // Fresh deserialize: not the same instance (binder may mutate BoundSymbol/OwningFile).
            ReferenceEquals(first, second).Should().BeFalse(
                "cache hits must deserialize a new tree so bind mutations do not leak");

            // Structural identity: same file key / hash / child count.
            second!.FileName.Should().Be(first!.FileName);
            second.ValueString.Should().Be(first.ValueString);
            second.AstChildren.Count.Should().Be(first.AstChildren.Count);
        }
        finally
        {
            AstCacheService.ClearMemory(tyhpdefPath);
        }
    }

    // Broken source still yields a non-null partial AST via ANTLR recovery. Caching that AST without
    // its diagnostics made the second parse (cache hit) report 0 errors — Story 13 P7 #2 / 14 P4 #2.
    private const string BrokenUnclosedBraceSource = "<?tyhp\nclass Broken {\n";

    [Fact]
    public void CompilationService_BrokenParse_IsNotCached_SecondParseStillReportsError()
    {
        using var scope = IsolatedAstCacheScope.Create(BrokenUnclosedBraceSource);
        try
        {
            AstCacheService.Clear();

            using var firstService = new CompilationService();
            var first = firstService.ParseFiles(
                [scope.SourceFilePath],
                new CompilationOptions
                {
                    EnableAstCache = true,
                    PhpVersion = "8.2",
                    ProjectPath = Path.GetDirectoryName(scope.SourceFilePath)!,
                    SkipChecking = true,
                });
            first.Diagnostics.HasErrors.Should().BeTrue(
                "first parse must surface the recoverable syntax error");
            first.ParsedFiles.Should().NotBeNull().And.NotBeEmpty(
                "ANTLR recovery still produces a partial AST");
            first.AstCacheMisses.Should().Be(1);

            // Persist any in-memory writes and drop memory so the second parse must hit disk (or
            // miss entirely — either way it must not silently succeed).
            AstCacheService.FlushMemory();
            AstCacheService.ClearMemory();

            using var secondService = new CompilationService();
            var second = secondService.ParseFiles(
                [scope.SourceFilePath],
                new CompilationOptions
                {
                    EnableAstCache = true,
                    PhpVersion = "8.2",
                    ProjectPath = Path.GetDirectoryName(scope.SourceFilePath)!,
                    SkipChecking = true,
                });
            second.Diagnostics.HasErrors.Should().BeTrue(
                "a broken parse must not be cached; the second run must re-parse and report the error");
            second.AstCacheHits.Should().Be(0, "errorful parses must not become cache hits");
            second.AstCacheMisses.Should().Be(1);
        }
        finally
        {
            AstCacheService.Clear();
        }
    }

    [Fact]
    public void TyhpdefParseContent_BrokenParse_IsNotCached_SecondParseStillReportsError()
    {
        using var scope = IsolatedAstCacheScope.Create(BrokenUnclosedBraceSource);
        try
        {
            AstCacheService.Clear();

            var firstDiagnostics = new Tyhp.Domain.Diagnostics.DiagnosticBag();
            var first = Tyhp.TyhpLang.Binder.BuiltIn.Tyhpdef.ParseContent(
                BrokenUnclosedBraceSource,
                scope.SourceFilePath,
                Tyhp.TyhpLang.Enum.ParseMode.Tyhp,
                firstDiagnostics);
            first.Should().NotBeNull("ANTLR recovery still produces a partial AST");
            firstDiagnostics.HasErrors.Should().BeTrue(
                "first parse must surface the recoverable syntax error");

            AstCacheService.FlushMemory();
            AstCacheService.ClearMemory();

            var secondDiagnostics = new Tyhp.Domain.Diagnostics.DiagnosticBag();
            var second = Tyhp.TyhpLang.Binder.BuiltIn.Tyhpdef.ParseContent(
                BrokenUnclosedBraceSource,
                scope.SourceFilePath,
                Tyhp.TyhpLang.Enum.ParseMode.Tyhp,
                secondDiagnostics);
            second.Should().NotBeNull();
            secondDiagnostics.HasErrors.Should().BeTrue(
                "Tyhpdef.ParseContent must not cache errorful parses; second call must re-report");

            // Confirm the entry was never stored (not merely wiped by ClearMemory).
            var hash = AstCacheService.ComputeContentHash(BrokenUnclosedBraceSource, tagless: false);
            AstCacheService.Get(scope.SourceFilePath, hash).Should().BeNull(
                "a parse that produced errors must leave no AstCache entry");
        }
        finally
        {
            AstCacheService.Clear();
        }
    }

    private static SrcFileAst ParseSource(string content, out System.Action cleanup)
    {
        var filePath = WriteTempFile(content, out cleanup);
        return ParseFile(filePath);
    }

    private static SrcFileAst ParseFile(string filePath)
    {
        using var compilationService = new CompilationService();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            PhpVersion = "8.2",
            ProjectPath = TestFileManager.GetRepoRoot(),
            TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            SkipChecking = true,
        };
        var result = compilationService.ParseFiles([filePath], options);
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        return result.ParsedFiles![0];
    }

    private static string WriteTempFile(string content, out System.Action cleanup)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);
        cleanup = () => { try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ } };
        return filePath;
    }

    private static int CountFoo(IBase2Ast root)
    {
        var count = 0;
        foreach (var node in EnumerateAll(root))
        {
            if (node is PhpObjectTypeDeclAst { Identifier: "Foo" })
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<IBase2Ast> EnumerateAll(IBase2Ast root)
    {
        yield return root;
        foreach (var child in root.AstChildren)
        {
            if (child == null)
            {
                continue;
            }

            foreach (var descendant in EnumerateAll(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Installs a throwaway <see cref="Project"/> with its own <c>cache-dir</c> and a source file
    /// under that project root, then restores the previous <see cref="Project.Singleton"/> on dispose.
    /// </summary>
    private sealed class IsolatedAstCacheScope : IDisposable
    {
        private readonly Project? _previousProject;
        private readonly string _rootDir;
        private bool _disposed;

        private IsolatedAstCacheScope(Project? previousProject, string rootDir, string sourceFilePath)
        {
            _previousProject = previousProject;
            _rootDir = rootDir;
            SourceFilePath = sourceFilePath;
        }

        public string SourceFilePath { get; }

        public static IsolatedAstCacheScope Create(string sourceContent)
        {
            var previous = Project.Singleton;
            var rootDir = Path.Combine(
                Path.GetTempPath(),
                "tyhp-tests",
                "ast-cache-isolated-" + Guid.NewGuid().ToString("N"));
            var projectDir = Path.Combine(rootDir, "project");
            var cacheDir = Path.Combine(rootDir, "cache");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(cacheDir);

            var tyhpJson = Path.Combine(projectDir, "tyhp.json");
            File.WriteAllText(tyhpJson, """{"include":["**/*.tyhp"]}""");
            var sourceFilePath = Path.Combine(projectDir, "test.tyhp");
            File.WriteAllText(sourceFilePath, sourceContent);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = tyhpJson,
                    ["cache-dir"] = cacheDir,
                })
                .Build();
            _ = new Project(configuration);

            return new IsolatedAstCacheScope(previous, rootDir, sourceFilePath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Project.Singleton = _previousProject;
            try { Directory.Delete(_rootDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class SymlinkAstCacheLayout : IDisposable
    {
        private SymlinkAstCacheLayout(
            string realRoot,
            string realProjectFile,
            string linkSourceFile)
        {
            RealRoot = realRoot;
            RealProjectFile = realProjectFile;
            LinkSourceFile = linkSourceFile;
        }

        public string RealRoot { get; }
        public string RealProjectFile { get; }
        public string LinkSourceFile { get; }

        public static SymlinkAstCacheLayout? TryCreate()
        {
            var id = Guid.NewGuid().ToString("N");
            var parent = Path.Combine(Path.GetTempPath(), "tyhp-symlink-astcache-" + id);
            var realRoot = Path.Combine(parent, "real");
            var linkRoot = Path.Combine(parent, "link");
            var srcDir = Path.Combine(realRoot, "src");
            Directory.CreateDirectory(srcDir);
            var realProjectFile = Path.Combine(realRoot, "tyhp.json");
            File.WriteAllText(realProjectFile, """{"include":["**/*.tyhp"]}""");
            File.WriteAllText(Path.Combine(srcDir, "index.tyhp"), "<?tyhp\n");

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

            return new SymlinkAstCacheLayout(
                realRoot,
                realProjectFile,
                Path.Combine(linkRoot, "src", "index.tyhp"));
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
