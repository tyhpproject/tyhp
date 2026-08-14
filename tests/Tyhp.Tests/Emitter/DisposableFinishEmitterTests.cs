using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class DisposableFinishEmitterTests
{
    [Fact]
    public void Emit_PropertyClosureCapturingThis_UsesWeakReference()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Emitter {
                public mixed $onReady = null;

                public function setup(): void {
                    $this->onReady = function() {
                        $this->emit('ready');
                    };
                }

                public function emit(string $event): void {}
            }
            """);

        php.Should().Contain("\\WeakReference::create($this)");
        php.Should().Contain("$this->onReady =");
        php.Should().Contain("use ($__weakSelf_");
        php.Should().Contain("->get()?->emit('ready')");
        php.Should().NotContain("$this->emit('ready')");
        php.Should().NotContain("$__weakSelf_0->get()?->onReady");
    }

    [Fact]
    public void Emit_CircularDisposableLocals_FallsBackToTryFinally()
    {
        var (php, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            class Node implements \Tyhp\Contracts\IsDisposable {
                public mixed $other = null;
                public function dispose(): void {}
            }

            function linkNodes(): void {
                $a := new Node();
                $b := new Node();
                $a->other = $b;
                $b->other = $a;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerDisposableCircularReference);
        php.Should().Contain("$a = null;");
        php.Should().Contain("$b = null;");
        php.Should().Contain("try {");
        php.Should().Contain("finally {");
        php.Should().Contain("$a->dispose()");
        php.Should().Contain("$b->dispose()");
        php.Should().NotContain("DisposableScope");
        php.Should().NotContain("->using(");
    }

    [Fact]
    public void Emit_TransitiveDisposableCycle_FallsBackToTryFinally()
    {
        var (php, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            class Node implements \Tyhp\Contracts\IsDisposable {
                public mixed $next = null;
                public function dispose(): void {}
            }

            function cycle3(): void {
                $a := new Node();
                $b := new Node();
                $c := new Node();
                $a->next = $b;
                $b->next = $c;
                $c->next = $a;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerDisposableCircularReference);
        php.Should().Contain("try {");
        php.Should().NotContain("DisposableScope");
    }

    [Fact]
    public void Emit_NestedDisposableInsideTryFinallyFallback_StillUsesDisposableScope()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Node implements \Tyhp\Contracts\IsDisposable {
                public mixed $other = null;
                public function dispose(): void {}
            }

            function linkNodes(): void {
                $a := new Node();
                $b := new Node();
                $a->other = $b;
                $b->other = $a;
                if (true) {
                    $inner := new Node();
                }
            }
            """);

        // Outer circular scope → try/finally; nested := must still use DisposableScope.
        php.Should().Contain("try {");
        php.Should().Contain("\\Tyhp\\DisposableScope::create()");
        php.Should().Contain("$inner = ");
        php.Should().Contain("->using(new Node())");
        php.Should().Contain("$a = null;");
    }

    [Fact]
    public void Emit_DisposableInsideClosure_ClosureOwnsItsScope()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Node implements \Tyhp\Contracts\IsDisposable {
                public function dispose(): void {}
            }

            function outer(): \Closure {
                return function(): void {
                    $inner := new Node();
                };
            }
            """);

        // The closure body must create and use its own DisposableScope.
        php.Should().Contain("\\Tyhp\\DisposableScope::create()");
        php.Should().Contain("->using(new Node())");
        // The scope var used inside the closure must be the one created inside it (regex ties them).
        php.Should().MatchRegex(@"function\s*\(\)\s*:\s*void\s*\{[\s\S]*?(\$__scope(?:_\d+)?) = \\Tyhp\\DisposableScope::create\(\);[\s\S]*?\1->using\(new Node\(\)\)");
    }

    [Fact]
    public void Emit_DisposableWithoutCycle_StillUsesDisposableScope()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Node implements \Tyhp\Contracts\IsDisposable {
                public function dispose(): void {}
            }

            function plain(): void {
                $a := new Node();
                $b := new Node();
            }
            """);

        php.Should().Contain("\\Tyhp\\DisposableScope::create()");
        php.Should().Contain("->using(new Node())");
        php.Should().NotContain("try {");
    }

    [Fact]
    public void Emit_ImplementsImportedContract_RootAnchorsFqcn()
    {
        // Alias / tyhpdef rewrites leave `Tyhp\Contracts\IsDisposable` without a leading `\`.
        // Inside another namespace that must still emit as an absolute PHP name.
        var php = CompileAndEmit("""
            <?tyhp
            namespace TestEmitter;

            use \Tyhp\Contracts\IsDisposable;

            class DbConnection implements IsDisposable {
                public function dispose(): void {}
            }
            """);

        php.Should().Contain("implements \\Tyhp\\Contracts\\IsDisposable");
        php.Should().NotContain("implements Tyhp\\Contracts\\IsDisposable");
    }

    [Fact]
    public void Emit_ParameterType_AppliesOutputNamespacePrefix()
    {
        // Binder FQNs use the source namespace; with output.namespacePrefix the emitted type
        // hint must include the prefix (and must not rewrite external tyhpdef types).
        var php = CompileAndEmit("""
            <?tyhp
            namespace TestEmitter;

            class Animal {}
            class Dog extends Animal {}

            function isDog(Animal $a): $a is Dog {
                return $a instanceof Dog;
            }

            function usesPromise(\Tyhp\Promise $p): \Tyhp\Promise {
                return $p;
            }
            """, namespacePrefix: "TyhpDebug");

        php.Should().Contain("namespace TyhpDebug\\TestEmitter;");
        php.Should().Contain("function isDog(\\TyhpDebug\\TestEmitter\\Animal $a): bool");
        php.Should().NotContain("function isDog(\\TestEmitter\\Animal $a)");
        php.Should().Contain("function usesPromise(\\Tyhp\\Promise $p): \\Tyhp\\Promise");
        php.Should().NotContain("\\TyhpDebug\\Tyhp\\Promise");
    }

    private static string CompileAndEmit(string tyhp, string? namespacePrefix = null)
    {
        var (php, diagnostics) = CompileAndEmitWithDiagnostics(tyhp, namespacePrefix);
        var unexpected = diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        unexpected.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", unexpected.Select(e => $"{e.Code}: {e.Message}"))}");
        return php;
    }

    private static (string Php, DiagnosticBag Diagnostics) CompileAndEmitWithDiagnostics(
        string tyhp,
        string? namespacePrefix = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "disposables-finish.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configValues = new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            };
            if (!string.IsNullOrWhiteSpace(namespacePrefix))
            {
                configValues["output:namespacePrefix"] = namespacePrefix;
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty(
                $"parse/bind produced no files. errors: {string.Join(", ", result.Diagnostics.Errors.Select(e => $"{e.Code}:{e.Message}"))}");

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                result.RequiresWeakReferenceCapture,
                result.RequiresDisposableTryFinally);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
            return (php, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
