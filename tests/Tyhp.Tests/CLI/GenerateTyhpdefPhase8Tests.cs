using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20")]
[Trait("Category", "CLI")]
public class GenerateTyhpdefPhase8Tests
{
    [Fact]
    public void GenerateFromTargetJsons_WritesGatedParsableTyhpdef()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-p8-json-").FullName;
        try
        {
            var (result, text) = GenerateMiniFromFixtures(outputDir);

            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().ContainSingle(p =>
                Path.GetFileName(p).Equals("ExtMini.tyhpdef", StringComparison.OrdinalIgnoreCase));
            text.Should().Contain("function mini_always");
            text.Should().Contain("declare(php=\">=8.4\")");
            text.Should().Contain("function mini_added");
            text.Should().Contain("declare(php=\"<8.4\")");
            text.Should().Contain("function mini_removed");
            text.Should().Contain("#[\\Tyhp\\Php(\">=8.4\")]");
            text.Should().Contain("function fresh()");

            var parsed = ParserTestHelper.ParseTyhpdefContent(text);
            parsed.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", parsed.Diagnostics.Errors.Select(e => e.Message)));
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void SnapshotReuse_DoesNotCallEnsure()
    {
        var work = Directory.CreateTempSubdirectory("tyhpdef-p8-reuse-").FullName;
        try
        {
            var snapshotDir = SeedMiniSnapshots(work);
            var outputDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outputDir);
            var manager = new ThrowingPhpRuntimeManager();
            var generator = new PhpDelegationTyhpdefGenerator(manager, new PhpRuntimeDetector());
            var result = new TyhpdefGenerationResult();
            generator.GenerateFromPhpTargets(
                MiniOptions(outputDir, snapshotDir, refresh: false),
                result,
                CancellationToken.None);

            manager.EnsureCalls.Should().Be(0);
            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().NotBeEmpty();
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public void RefreshSnapshots_ForcesEnsure()
    {
        var work = Directory.CreateTempSubdirectory("tyhpdef-p8-refresh-").FullName;
        try
        {
            var snapshotDir = SeedMiniSnapshots(work);
            var outputDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outputDir);
            var manager = new FailingPhpRuntimeManager();
            var generator = new PhpDelegationTyhpdefGenerator(manager, new PhpRuntimeDetector());
            var result = new TyhpdefGenerationResult();
            generator.GenerateFromPhpTargets(
                MiniOptions(outputDir, snapshotDir, refresh: true),
                result,
                CancellationToken.None);

            manager.EnsureCalls.Should().BeGreaterThan(0);
            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefPhpRuntimeDownloadFailed);
            result.GeneratedFiles.Should().BeEmpty();
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public void FailClosed_SecondTargetMissing_DoesNotMerge()
    {
        var work = Directory.CreateTempSubdirectory("tyhpdef-p8-fail-").FullName;
        try
        {
            var snapshotDir = Path.Combine(work, "snapshots");
            Directory.CreateDirectory(Path.Combine(snapshotDir, "8.2"));
            File.Copy(
                TyhpdefGenFixtures.Snapshot82MiniPath,
                Path.Combine(snapshotDir, "8.2", "mini.json"),
                overwrite: true);
            var outputDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outputDir);
            var generator = new PhpDelegationTyhpdefGenerator(
                new FailingPhpRuntimeManager(),
                new PhpRuntimeDetector());
            var result = new TyhpdefGenerationResult();
            generator.GenerateFromPhpTargets(
                MiniOptions(outputDir, snapshotDir, refresh: false),
                result,
                CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefPhpRuntimeDownloadFailed);
            result.GeneratedFiles.Should().BeEmpty();
            Directory.EnumerateFiles(outputDir, "*.tyhpdef", SearchOption.AllDirectories)
                .Should()
                .BeEmpty();
        }
        finally
        {
            TryDelete(work);
        }
    }

    [Fact]
    public void Regen_DoesNotTouchHandOverlay()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-p8-hand-").FullName;
        try
        {
            var overlayDir = Path.Combine(outputDir, "_tyhpdef", "overlays");
            Directory.CreateDirectory(overlayDir);
            var handPath = Path.Combine(overlayDir, "hand.tyhpdef");
            const string sentinel = "<?tyhpdef\n// hand-authored overlay — do not wipe\nfunction keep_me(): void;\n";
            File.WriteAllText(handPath, sentinel);

            var (result, _) = GenerateMiniFromFixtures(outputDir);
            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));

            File.ReadAllText(handPath).Should().Be(sentinel);
            TyhpdefSnapshotStore.IsHandOverlayPath(handPath).Should().BeTrue();
            TyhpdefSnapshotStore.IsHandOverlayPath(
                Path.Combine(overlayDir, "stubs", "Ext.Mini.tyhpdef")).Should().BeFalse();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void EmittedTyhpdef_BindsDifferentApisAt82And84()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-p8-bind-").FullName;
        try
        {
            var (result, text) = GenerateMiniFromFixtures(outputDir);
            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));

            using var builder = new TestProjectBuilder();
            WriteConsumer(builder, text);

            var project = builder.BuildProject();
            var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
            var at82 = BindConsumer(project, userFile, "8.2");
            var at84 = BindConsumer(project, userFile, "8.4");

            at82.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", at82.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
            at84.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", at84.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

            FindFunction(at82.Global, "mini_always").Should().NotBeNull();
            FindFunction(at84.Global, "mini_always").Should().NotBeNull();
            FindFunction(at82.Global, "mini_added").Should().BeNull();
            FindFunction(at84.Global, "mini_added").Should().NotBeNull();
            FindFunction(at82.Global, "mini_removed").Should().NotBeNull();
            FindFunction(at84.Global, "mini_removed").Should().BeNull();

            var shared82 = FindObject(at82.Global, "MiniShared");
            var shared84 = FindObject(at84.Global, "MiniShared");
            shared82.Should().NotBeNull();
            shared84.Should().NotBeNull();
            FindMethod(shared82!, "always").Should().NotBeNull();
            FindMethod(shared84!, "always").Should().NotBeNull();
            FindMethod(shared82!, "fresh").Should().BeNull();
            FindMethod(shared84!, "fresh").Should().NotBeNull();
            FindMethod(shared82!, "legacy").Should().NotBeNull();
            FindMethod(shared84!, "legacy").Should().BeNull();

            FindObject(at82.Global, "MiniAdded").Should().BeNull();
            FindObject(at84.Global, "MiniAdded").Should().NotBeNull();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void ManagedPhpTargets_WhenCachePresent_ReflectsWithoutUserBinary()
    {
        if (!HasCachedMinor("8.2") || !HasCachedMinor("8.4"))
        {
            return;
        }

        var work = Directory.CreateTempSubdirectory("tyhpdef-p8-live-").FullName;
        try
        {
            var outputDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outputDir);
            var snapshotDir = Path.Combine(work, "snapshots");
            var result = new TyhpdefGenerationResult();
            new PhpDelegationTyhpdefGenerator().GenerateFromPhpTargets(
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    ExtensionName = "json",
                    PhpTargets = ["8.2", "8.4"],
                    OutputDirectory = outputDir,
                    SnapshotDirectory = snapshotDir,
                    IncludeDocComments = false,
                    IncludeDeprecated = true,
                    Overwrite = true,
                    Split = "file",
                    FetchStubCache = false,
                    StubCacheDirectory = MissingStubCache(),
                    NoPhpRuntimeUpdate = true,
                    PhpExecutablePath = null,
                },
                result,
                CancellationToken.None);

            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().NotBeEmpty();
            File.Exists(TyhpdefSnapshotStore.FilePath(snapshotDir, "8.2", "json")).Should().BeTrue();
            File.Exists(TyhpdefSnapshotStore.FilePath(snapshotDir, "8.4", "json")).Should().BeTrue();
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static (TyhpdefGenerationResult Result, string Text) GenerateMiniFromFixtures(string outputDir)
    {
        var result = new TyhpdefGenerationResult();
        new PhpDelegationTyhpdefGenerator().GenerateFromTargetJsons(
            [
                ("8.2", File.ReadAllText(TyhpdefGenFixtures.Snapshot82MiniPath)),
                ("8.4", File.ReadAllText(TyhpdefGenFixtures.Snapshot84MiniPath)),
            ],
            MiniOptions(outputDir, snapshotDir: Path.Combine(outputDir, "unused-snapshots"), refresh: false),
            result,
            CancellationToken.None);
        var path = result.GeneratedFiles.FirstOrDefault(p =>
            Path.GetFileName(p).Equals("ExtMini.tyhpdef", StringComparison.OrdinalIgnoreCase));
        var text = path != null && File.Exists(path) ? File.ReadAllText(path) : "";
        return (result, text);
    }

    private static TyhpdefGenerationOptions MiniOptions(string outputDir, string snapshotDir, bool refresh)
        => new()
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = "mini",
            PhpTargets = ["8.2", "8.4"],
            OutputDirectory = outputDir,
            SnapshotDirectory = snapshotDir,
            RefreshSnapshots = refresh,
            IncludeDocComments = false,
            IncludeDeprecated = true,
            Overwrite = true,
            Split = "file",
            FetchStubCache = false,
            StubCacheDirectory = MissingStubCache(),
            NoPhpRuntimeUpdate = true,
        };

    private static string SeedMiniSnapshots(string work)
    {
        var snapshotDir = Path.Combine(work, "snapshots");
        Directory.CreateDirectory(Path.Combine(snapshotDir, "8.2"));
        Directory.CreateDirectory(Path.Combine(snapshotDir, "8.4"));
        File.Copy(
            TyhpdefGenFixtures.Snapshot82MiniPath,
            Path.Combine(snapshotDir, "8.2", "mini.json"),
            overwrite: true);
        File.Copy(
            TyhpdefGenFixtures.Snapshot84MiniPath,
            Path.Combine(snapshotDir, "8.4", "mini.json"),
            overwrite: true);
        return snapshotDir;
    }

    private static void WriteConsumer(TestProjectBuilder builder, string tyhpdef)
    {
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./vendor/tyhpdef/acme-stubs/composer.json");
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpdefPackageComposer("vendor/tyhpdef/acme-stubs", """
            {
                "include": ["./*.tyhpdef"]
            }
            """, packageName: "tyhpdef/acme-stubs");
        builder.WithTyhpFile("vendor/tyhpdef/acme-stubs/ExtMini.tyhpdef", tyhpdef);
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) BindConsumer(
        Project project,
        string userFile,
        string phpVersion)
    {
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.PhpVersion = phpVersion;
            o.PhpVersionWasDefaulted = false;
            o.EnableAstCache = false;
            o.SkipChecking = true;
        });
        var result = compilationService.ParseFiles([userFile], options);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol func
                && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = func;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static ObjectMethodSymbol? FindMethod(ObjectDeclarationSymbol obj, string name)
    {
        if (obj.Members.TryGetValue(name, out var member) && member is ObjectMethodSymbol method)
        {
            return method;
        }

        if (obj.ContainingScope is not IBaseScope scope)
        {
            return null;
        }

        foreach (var childScope in scope.GetAllChildScopes())
        {
            if (childScope.DeclarationSymbol is ObjectDeclarationSymbol same
                && ReferenceEquals(same, obj))
            {
                foreach (var symbol in childScope.GetAllChildSymbols())
                {
                    if (symbol is ObjectMethodSymbol found
                        && string.Equals(found.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }

    private static bool HasCachedMinor(string minor)
    {
        var root = PhpRuntimeManager.ResolveCacheRoot(null);
        if (!Directory.Exists(root))
        {
            return false;
        }

        try
        {
            foreach (var ridDir in Directory.EnumerateDirectories(root))
            {
                var minorDir = Path.Combine(ridDir, minor);
                if (!Directory.Exists(minorDir))
                {
                    continue;
                }

                if (Directory.EnumerateFiles(minorDir, "php", SearchOption.AllDirectories).Any()
                    || Directory.EnumerateFiles(minorDir, "php.exe", SearchOption.AllDirectories).Any())
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static string MissingStubCache()
        => Path.Combine(Path.GetTempPath(), "tyhp-no-stubs-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class ThrowingPhpRuntimeManager : PhpRuntimeManager
    {
        public int EnsureCalls { get; private set; }

        public override PhpRuntimeInfo? Ensure(
            string minor,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken = default)
        {
            this.EnsureCalls++;
            throw new InvalidOperationException(
                "PhpRuntimeManager.Ensure must not run when snapshots are reused (" + minor + ")");
        }
    }

    private sealed class FailingPhpRuntimeManager : PhpRuntimeManager
    {
        public int EnsureCalls { get; private set; }

        public override PhpRuntimeInfo? Ensure(
            string minor,
            TyhpdefGenerationOptions options,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken = default)
        {
            this.EnsureCalls++;
            diagnostics.AddError(
                MessageCode.TyhpdefPhpRuntimeDownloadFailed,
                "generate_tyhpdef",
                0,
                0,
                "test-blocked " + minor);
            return null;
        }
    }
}
