using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class AsyncAwaitFinishEmitterTests
{
    [Fact]
    public void Emit_AsyncForeach_PromiseIterable_UsesForeachAwait()
    {
        var php = EmitWithKinds(
            """
            <?tyhp
            async function sum(array<int> $values): int {
                int $total = 0;
                foreach (await loadAll() as $item) {
                    $total += $item;
                }
                return $total;
            }
            """,
            AsyncForeachKind.PromiseIterable);

        php.Should().Contain("foreach (\\Tyhp\\Promise::_await(");
        php.Should().Contain(" as $item)");
        php.Should().NotContain("getAsyncIterator");
        php.Should().NotContain("$__asyncIter_");
    }

    [Fact]
    public void Emit_AsyncForeach_AsyncIterable_UsesWhileLoop()
    {
        var php = EmitWithKinds(
            """
            <?tyhp
            async function drain($queue): void {
                foreach (await $queue as $message) {
                    process($message);
                }
            }
            """,
            AsyncForeachKind.AsyncIterable);

        php.Should().Contain("$__asyncIter_1 = $queue->getAsyncIterator();");
        php.Should().Contain("while (\\Tyhp\\Promise::_await($__asyncIter_1->next()))");
        php.Should().Contain("$message = \\Tyhp\\Promise::_await($__asyncIter_1->current());");
        php.Should().NotContain("foreach (\\Tyhp\\Promise::_await");
    }

    [Fact]
    public void Emit_AsyncForeach_PromiseAsyncIterable_AwaitsThenIterates()
    {
        var php = EmitWithKinds(
            """
            <?tyhp
            async function drain($connect): void {
                foreach (await $connect() as $item) {
                    process($item);
                }
            }
            """,
            AsyncForeachKind.PromiseAsyncIterable);

        php.Should().Contain("$__asyncIter_1 = \\Tyhp\\Promise::_await(");
        php.Should().Contain(")->getAsyncIterator();");
        php.Should().Contain("while (\\Tyhp\\Promise::_await($__asyncIter_1->next()))");
        php.Should().Contain("$item = \\Tyhp\\Promise::_await($__asyncIter_1->current());");
    }

    [Fact]
    public void Emit_AsyncForeach_KeyValue_UsesCurrentKeyAndValue()
    {
        var php = EmitWithKinds(
            """
            <?tyhp
            async function dump($stream): void {
                foreach (await $stream as $key => $value) {
                    echo $key;
                }
            }
            """,
            AsyncForeachKind.AsyncIterable);

        php.Should().Contain("$key = \\Tyhp\\Promise::_await($__asyncIter_1->currentKey());");
        php.Should().Contain("$value = \\Tyhp\\Promise::_await($__asyncIter_1->currentValue());");
        php.Should().NotContain("->current();");
    }

    [Fact]
    public void Emit_NestedAsyncForeach_UsesUniqueTempNames()
    {
        var php = EmitWithKinds(
            """
            <?tyhp
            async function nested($outer, $inner): void {
                foreach (await $outer as $a) {
                    foreach (await $inner as $b) {
                        usePair($a, $b);
                    }
                }
            }
            """,
            AsyncForeachKind.AsyncIterable,
            AsyncForeachKind.AsyncIterable);

        php.Should().Contain("$__asyncIter_1 =");
        php.Should().Contain("$__asyncIter_2 =");
        php.Should().Contain("while (\\Tyhp\\Promise::_await($__asyncIter_1->next()))");
        php.Should().Contain("while (\\Tyhp\\Promise::_await($__asyncIter_2->next()))");
    }

    [Fact]
    public void Emit_AsyncArrow_WrapsWithPromiseAsync()
    {
        var php = EmitOnly("""
            <?tyhp
            $fn = async fn(int $x): int => await doubleAsync($x);
            """);

        php.Should().Contain("fn(int $x): \\Tyhp\\Promise => \\Tyhp\\Promise::_async(fn() =>");
        php.Should().Contain("\\Tyhp\\Promise::_await(doubleAsync($x))");
        php.Should().NotContain("async fn");
        php.Should().NotContain("async function");
    }

    [Fact]
    public void Emit_AsyncArrow_PreservesFreeVariableCapture()
    {
        // A `function() use (...)` desugaring would drop `$factor`; nested PHP arrows auto-capture it.
        var php = EmitOnly("""
            <?tyhp
            $factor = 3;
            $fn = async fn(int $x): int => await scaleAsync($x, $factor);
            """);

        php.Should().Contain("fn(int $x): \\Tyhp\\Promise => \\Tyhp\\Promise::_async(fn() =>");
        php.Should().Contain("scaleAsync($x, $factor)");
        // No explicit `use (...)` needed — arrows capture automatically.
        php.Should().NotContain("use (");
    }

    [Fact]
    public void Emit_AsyncClosure_WrapsWithPromiseAsync()
    {
        var php = EmitOnly("""
            <?tyhp
            $fn = async function(int $x) use ($y): int {
                return await combineAsync($x, $y);
            };
            """);

        php.Should().Contain("function (int $x) use ($y): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function () use ($y, $x): int");
        php.Should().Contain("\\Tyhp\\Promise::_await(combineAsync($x, $y))");
        php.Should().NotContain("async function");
    }

    [Fact]
    public void Emit_TopLevelAwait_Application_WrapsWithPromiseRun()
    {
        var php = EmitOnly("""
            <?tyhp
            $result = await fetchAsync();
            echo $result;
            """, projectType: ProjectType.Application);

        php.Should().Contain("\\Tyhp\\Promise::run(function () {");
        php.Should().Contain("\\Tyhp\\Promise::_await(fetchAsync())");
        php.Should().Contain("echo $result;");
    }

    [Fact]
    public void Emit_TopLevelAwait_Library_SkipsPromiseRun()
    {
        var php = EmitOnly("""
            <?tyhp
            $result = await fetchAsync();
            echo $result;
            """, projectType: ProjectType.Library);

        php.Should().NotContain("Promise::run");
        php.Should().Contain("\\Tyhp\\Promise::_await(fetchAsync())");
    }

    private static string EmitOnly(string content, ProjectType? projectType = null)
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(content);
        parseResult.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", parseResult.Diagnostics)}");
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        Project? project = null;
        if (projectType is { } type)
        {
            project = CreateProject(type);
        }

        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag(), project);
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }

    private static string EmitWithKinds(string content, params AsyncForeachKind[] kinds)
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(content);
        parseResult.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", parseResult.Diagnostics)}");
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;

        var loops = new List<PhpLoopAst>();
        CollectForeachLoops(srcFile, loops);
        loops.Should().HaveCount(kinds.Length,
            $"expected {kinds.Length} foreach loop(s), found {loops.Count}");

        var kindMap = new Dictionary<PhpLoopAst, AsyncForeachKind>();
        for (var i = 0; i < kinds.Length; i++)
        {
            kindMap[loops[i]] = kinds[i];
        }

        var context = EmitContext.Create(
            new GlobalScope(),
            new DiagnosticBag(),
            project: null,
            requiresRuntimeGenericTracking: null,
            requiresWeakReferenceCapture: null,
            requiresDisposableTryFinally: null,
            asyncForeachKinds: kindMap);
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }

    private static void CollectForeachLoops(IBase2Ast node, List<PhpLoopAst> loops)
    {
        if (node is PhpLoopAst { LoopType: PhpLoopType.Foreach } loop)
        {
            loops.Add(loop);
        }

        foreach (var child in node.AstChildren)
        {
            if (child is not null)
            {
                CollectForeachLoops(child, loops);
            }
        }
    }

    private static Project CreateProject(ProjectType type)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = type == ProjectType.Library ? "library" : "application",
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }
}
