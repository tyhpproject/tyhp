using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Analysis;
using Tyhp.LanguageServer.Configuration;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class LspMemoryTests
{
    private const string ProjectJson = """
        {
            "include": ["src/**/*.tyhp"],
            "tyhpdefInclude": ["stubs/*.tyhpdef"],
            "output": { "path": "build/" }
        }
        """;

    [Fact]
    public void TyhpdefSourceCache_ReusesSourcesAndClearsBindStateBetweenBinds()
    {
        using var compilation = new CompilationService();
        var cache = new TyhpdefSourceCache();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            ProjectPath = Path.GetTempPath(),
            SkipChecking = true,
            TyhpdefSourceProvider = cache,
        };

        IReadOnlyList<TyhpdefSourceFile> sources = cache.GetSourceFiles(new DiagnosticBag(), options);
        sources.Should().NotBeEmpty();

        var diagnostics = new DiagnosticBag();
        SrcFileAst? ast = compilation.ParseFromContent(
            "<?tyhp\nfunction size(): int { return \\strlen('x'); }\n",
            "size.tyhp",
            diagnostics,
            options);
        new TyhpBinder(diagnostics, options).Bind([ast!]).Should().NotBeNull();

        cache.LoadCount.Should().Be(1);
        CountBoundNodes(sources).Should().BeGreaterThan(0);

        IReadOnlyList<TyhpdefSourceFile> reused = cache.GetSourceFiles(new DiagnosticBag(), options);
        reused.Should().BeSameAs(sources);
        cache.LoadCount.Should().Be(1);
        CountBoundNodes(reused).Should().Be(0);
    }

    [Fact]
    public void TyhpdefSourceCache_BindsSameDiagnosticsOnReuse()
    {
        var cache = new TyhpdefSourceCache();
        const string source = "<?tyhp\nfunction size(): string { return \\strlen('x'); }\n";

        string Run(ITyhpdefSourceProvider? provider)
        {
            using var compilation = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                ProjectPath = Path.GetTempPath(),
                TyhpdefSourceProvider = provider,
            };
            var diagnostics = new DiagnosticBag();
            SrcFileAst? ast = compilation.ParseFromContent(source, "size.tyhp", diagnostics, options);
            var scope = new TyhpBinder(diagnostics, options).Bind([ast!])!;
            new TyhpChecker(diagnostics, new SymbolTree(scope), scope, options.Checker).Check([ast!]);
            return string.Join("\n", diagnostics.All.Select(d => $"{d.Code} {d.Message}").Order(StringComparer.Ordinal));
        }

        string fresh = Run(provider: null);
        fresh.Should().NotBeEmpty();
        Run(cache).Should().Be(fresh);
        Run(cache).Should().Be(fresh);
        cache.LoadCount.Should().Be(1);
    }

    [Fact]
    public void TyhpdefSourceCache_ReloadsWhenTyhpdefFileChangesOrAfterInvalidate()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson(ProjectJson)
            .WithTyhpFile("stubs/ext.tyhpdef", "<?tyhpdef\nfunction ext_one(): int;\n")
            .WithTyhpFile("src/Main.tyhp", "<?tyhp\nfunction main(): void {}\n");
        var options = CompilationOptions.FromProject(project.BuildProject());
        options.EnableAstCache = false;
        var cache = new TyhpdefSourceCache();

        IReadOnlyList<TyhpdefSourceFile> first = cache.GetSourceFiles(new DiagnosticBag(), options);
        cache.GetSourceFiles(new DiagnosticBag(), options).Should().BeSameAs(first);
        cache.LoadCount.Should().Be(1);

        string stub = Path.Combine(project.ProjectDirectory, "stubs", "ext.tyhpdef");
        File.WriteAllText(stub, "<?tyhpdef\nfunction ext_one(): int;\nfunction ext_two(): int;\n");
        File.SetLastWriteTimeUtc(stub, DateTime.UtcNow.AddMinutes(1));
        cache.GetSourceFiles(new DiagnosticBag(), options).Should().NotBeSameAs(first);
        cache.LoadCount.Should().Be(2);

        cache.Invalidate();
        cache.GetSourceFiles(new DiagnosticBag(), options);
        cache.LoadCount.Should().Be(3);
    }

    [Fact]
    public async Task IdleMemoryCompactor_CollectsOnceAfterWork()
    {
        int collections = 0;
        using var compactor = new IdleMemoryCompactor(
            TimeSpan.FromMilliseconds(30),
            isIdle: () => true,
            collect: () => Interlocked.Increment(ref collections));

        await Task.Delay(150);
        Volatile.Read(ref collections).Should().Be(0);

        compactor.NotifyWork();
        await WaitUntilAsync(() => Volatile.Read(ref collections) == 1);
        await Task.Delay(150);
        Volatile.Read(ref collections).Should().Be(1);
    }

    [Fact]
    public async Task IdleMemoryCompactor_WaitsWhileBusy()
    {
        int collections = 0;
        bool idle = false;
        using var compactor = new IdleMemoryCompactor(
            TimeSpan.FromMilliseconds(30),
            isIdle: () => Volatile.Read(ref idle),
            collect: () => Interlocked.Increment(ref collections));

        compactor.NotifyWork();
        await Task.Delay(150);
        Volatile.Read(ref collections).Should().Be(0);

        Volatile.Write(ref idle, true);
        await WaitUntilAsync(() => Volatile.Read(ref collections) == 1);
    }

    [Fact]
    public void IdleMemoryCompactor_ZeroDelayDisablesCollection()
    {
        int collections = 0;
        using var compactor = new IdleMemoryCompactor(
            TimeSpan.Zero,
            isIdle: () => true,
            collect: () => Interlocked.Increment(ref collections));

        compactor.NotifyWork();
        Thread.Sleep(100);
        Volatile.Read(ref collections).Should().Be(0);
    }

    [Fact]
    public async Task Watcher_RootsAtProjectAndIgnoresFilesOutsideInclude()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson(ProjectJson)
            .WithTyhpFile("src/Main.tyhp", "<?tyhp\nnamespace App;\nfunction main(): void {}\n");
        var config = new ServerConfiguration
        {
            DebounceDelay = 40,
            IdleCompactionDelay = 0,
            Project = project.BuildProject(),
            CompilationOptions = CompilationOptions.FromProject(project.BuildProject()),
        };
        config.CompilationOptions.EnableAstCache = false;

        await using var session = await LspTestSession.StartAsync(configuration: config);
        string parent = Path.GetDirectoryName(project.ProjectDirectory)!;
        await session.InitializeAsync(new Uri(parent + Path.DirectorySeparatorChar));
        await session.NotifyInitializedAsync();

        AnalysisService analysis = session.Server.Analysis;
        await session.WaitForAsync(() => HasProjectFile(analysis, "Main.tyhp") && analysis.IsWatching);
        analysis.ResolveWatchRoot().Should().Be(Path.GetFullPath(project.ProjectDirectory));
        await Task.Delay(500);

        Directory.CreateDirectory(Path.Combine(project.ProjectDirectory, "scratch"));
        File.WriteAllText(
            Path.Combine(project.ProjectDirectory, "scratch", "Foreign.tyhp"),
            "<?tyhp\nnamespace Scratch;\nfunction foreign(): void {}\n");
        File.WriteAllText(
            Path.Combine(project.ProjectDirectory, "src", "Added.tyhp"),
            "<?tyhp\nnamespace App;\nfunction added(): void {}\n");

        await session.WaitForAsync(() => HasProjectFile(analysis, "Added.tyhp"), TimeSpan.FromSeconds(15));
        await Task.Delay(300);
        HasProjectFile(analysis, "Foreign.tyhp").Should().BeFalse();
    }

    [Fact]
    public async Task AnalysisCycle_ClearsBindStateLeftOnDocumentsThatWereNotReparsed()
    {
        using var project = new TestProjectBuilder();
        const string box = """
            <?tyhp
            namespace App;
            global use extension \App\CollectionExtensions;
            extension CollectionExtensions {
                extends<TKey extends int|string, TValue> array<TKey, TValue> {
                    function countAll(): int { return \count($this); }
                }
            }
            """;
        const string main = """
            <?tyhp
            namespace App;
            function main(): int {
                array<int, string> $items = [1 => 'a'];
                return $items->countAll();
            }
            """;
        project
            .WithTyhpJson(ProjectJson)
            .WithTyhpFile("src/Box.tyhp", box)
            .WithTyhpFile("src/Main.tyhp", main);
        var config = new ServerConfiguration
        {
            DebounceDelay = 40,
            IdleCompactionDelay = 0,
            Project = project.BuildProject(),
            CompilationOptions = CompilationOptions.FromProject(project.BuildProject()),
        };
        config.CompilationOptions.EnableAstCache = false;

        await using var session = await LspTestSession.StartAsync(configuration: config);
        await session.InitializeAsync(new Uri(project.ProjectDirectory + Path.DirectorySeparatorChar));
        await session.NotifyInitializedAsync();
        AnalysisService analysis = session.Server.Analysis;
        await session.WaitForAsync(() => HasProjectFile(analysis, "Main.tyhp"));

        var boxUri = new Uri(Path.Combine(project.ProjectDirectory, "src", "Box.tyhp"));
        var mainUri = new Uri(Path.Combine(project.ProjectDirectory, "src", "Main.tyhp"));
        await OpenAsync(session, boxUri, box);
        await OpenAsync(session, mainUri, main);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(mainUri) is not null);

        await analysis.AnalyzeDocumentAsync(mainUri);
        IBaseSymbol previousInt = ((IBaseScope)analysis.GetGlobalScope()!).FindChildSymbolByName("int")!;
        previousInt.Should().NotBeNull();
        SrcFileAst boxAst = session.Server.Workspace.GetDocument(boxUri)!.ParsedAst!;
        IBase2Ast unbound = Descendants(boxAst).Skip(1).First(node => node.BoundSymbol is null);
        unbound.BoundSymbol = previousInt;

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidChangeName,
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier { Uri = mainUri, Version = 2 },
                ContentChanges = [new TextDocumentContentChangeEvent { Text = main + "\n// edit\n" }],
            });
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(mainUri)!.Version == 2);
        await analysis.AnalyzeDocumentAsync(mainUri);

        session.Server.Workspace.GetDocument(boxUri)!.ParsedAst.Should().BeSameAs(boxAst);
        ReferenceEquals(unbound.BoundSymbol, previousInt).Should().BeFalse();
    }

    private static Task OpenAsync(LspTestSession session, Uri uri, string text)
        => session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "tyhp", Version = 1, Text = text },
            });

    private static IEnumerable<IBase2Ast> Descendants(IBase2Ast root)
    {
        var pending = new Stack<IBase2Ast>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            IBase2Ast node = pending.Pop();
            yield return node;
            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static bool HasProjectFile(AnalysisService analysis, string fileName)
        => analysis.GetProjectAsts().Any(ast =>
            string.Equals(Path.GetFileName(ast.FileName), fileName, StringComparison.OrdinalIgnoreCase));

    private static int CountBoundNodes(IEnumerable<TyhpdefSourceFile> sources)
    {
        int bound = 0;
        var pending = new Stack<IBase2Ast>();
        foreach (TyhpdefSourceFile source in sources)
        {
            pending.Push(source.Ast);
        }

        while (pending.Count > 0)
        {
            IBase2Ast node = pending.Pop();
            if (node.BoundSymbol is not null || node.OwningFile is not null)
            {
                bound++;
            }

            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    pending.Push(child);
                }
            }

            foreach (IBase2Ast attribute in node.AstAttributes)
            {
                pending.Push(attribute);
            }

            foreach (IBase2Ast addon in node.AstGrammarAddons.Values)
            {
                pending.Push(addon);
            }
        }

        return bound;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met.");
            }

            await Task.Delay(10);
        }
    }
}
