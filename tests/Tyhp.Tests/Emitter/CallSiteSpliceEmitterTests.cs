using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.Splice;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
[Trait("Category", "Story20.6")]
public class CallSiteSpliceEmitterTests
{
    [Fact]
    public void Emit_SubstitutesReceiverAndArgs_Parenthesized()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting extends Money {
                function format(string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            function show(Money $m): string {
                return $m->format('USD');
            }
            """);

        php.Should().Contain("$m->amount");
        php.Should().Contain("'USD'");
        php.Should().NotContain("$m->format(");
        php.Should().NotContain(@"\MoneyFormatting::format($m, 'USD')");
    }

    [Fact]
    public void Emit_NestedSplice_ReducesToFixpoint()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension MathHelpers extends int {
                function doubled(): int {
                    return $this + $this;
                }
                function quadrupled(): int {
                    return $this->doubled()->doubled();
                }
            }
            function f(int $n): int {
                return $n->quadrupled();
            }
            """);

        php.Should().NotContain("$n->quadrupled(");
        php.Should().NotContain(@"::quadrupled($n");
        php.Should().Contain("__tyhpInlineTemp");
        php.Should().Contain("+");
    }

    [Fact]
    public void Emit_RepeatedSimpleReceiver_SkipsLocal()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension MathHelpers extends int {
                function doubled(): int {
                    return $this + $this;
                }
            }
            function f(int $n): int {
                return $n->doubled();
            }
            """);

        php.Should().Contain("$n");
        php.Should().Contain("+");
        php.Should().NotContain("$n->doubled(");
        php.Should().NotContain("__tyhpInlineTemp");
    }

    [Fact]
    public void Emit_RepeatedCallReceiver_HoistsByValue()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension MathHelpers extends int {
                function doubled(): int {
                    return $this + $this;
                }
            }
            function tick(): int {
                return 1;
            }
            function f(int $n): int {
                return ($n + tick())->doubled();
            }
            """);

        php.Should().Contain("__tyhpInlineTemp");
        php.Should().Contain("=");
        php.Should().NotContain("->doubled(");
    }

    [Fact]
    public void Emit_RepeatedByRefProperty_HoistsRefBound()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public int $n = 0;
            }
            extension Mut extends Holder {
                function bump(int &$n): int {
                    return $n = $n + $n;
                }
            }
            function f(Holder $h): int {
                return $h->bump($h->n);
            }
            """);

        php.Should().Contain("__tyhpInlineTemp");
        php.Should().Contain("&$h->n");
        php.Should().NotContain("->bump(");
    }

    [Fact]
    public void Emit_RepeatedByRefThis_HoistsRefBound()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public array $data = [];
            }
            extension MutArray extends array {
                function pushTwice(&$this, int $a, int $b): int {
                    return \array_push($this, $a) + \array_push($this, $b);
                }
            }
            function f(Holder $h): int {
                return $h->data->pushTwice(1, 2);
            }
            """);

        php.Should().Contain("__tyhpInlineTemp");
        php.Should().Contain("&$h->data");
        php.Should().NotContain("->pushTwice(");
    }

    [Fact]
    public void Emit_PhpBackerUnspliceable_KeepsRealCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension Keep extends int {
                function ignore(int $n): int {
                    return $this;
                }
            }
            function tick(): int {
                return 1;
            }
            function f(int $x): int {
                return $x->ignore(tick());
            }
            """);

        php.Should().Contain(@"\Keep::ignore(");
        php.Should().Contain("tick()");
    }

    [Fact]
    public void Tyhpdef_ClassBodyThinFn_SplicesReceiverAndOmitsSyntheticClass()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Box {
                extension fn toUpper(): string => \strtoupper($this);
            }
            """,
            """
            <?tyhp
            function f(Box $name): string {
                return $name->toUpper();
            }
            """);

        php.Should().Contain(@"\strtoupper($name)");
        php.Should().NotContain("$name->toUpper(");
        php.Should().NotContain("__TyhpInlineExt_");
        php.Should().NotContain("::toUpper(");
    }

    [Fact]
    public void Tyhpdef_ClassBodyThinOperator_SplicesToMappedExpression()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Money {
                public function plus(Money $other): Money;
                extension operator +(self $l, self $r): self => $l->plus($r);
            }
            """,
            """
            <?tyhp
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a->plus($b)");
        php.Should().NotContain("$a + $b");
        php.Should().NotContain("__add");
        php.Should().NotContain("__TyhpInlineExt_");
    }

    [Fact]
    public void Emit_NestedReduction_DoesNotInlineOrdinaryClassMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function plus(Money $other): Money {
                    return $this;
                }
            }
            extension MoneyOps extends Money {
                operator + (self $left, self $right): Money => $left->plus($right);
            }
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a->plus($b)");
        php.Should().NotContain("return $a;");
        php.Should().NotContain("$a + $b");
    }

    [Fact]
    public void Tyhpdef_ClassBodyThinFn_SplicesDefaultArgument()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Box {
                extension fn greet(string $suffix = '!'): string => \strtoupper($this) . $suffix;
            }
            """,
            """
            <?tyhp
            function f(Box $name): string {
                return $name->greet();
            }
            """);

        php.Should().Contain(@"\strtoupper($name)");
        php.Should().Contain("!");
        php.Should().NotContain("$name->greet(");
        php.Should().NotContain("__TyhpInlineExt_");
    }

    [Fact]
    public void Tyhpdef_ArrayMutatingThis_SplicesToPhpBuiltins()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            extension MutArray extends array {
                fn sort(&$this, int $flags = 0): bool => \sort($this, $flags);
                fn push(&$this, mixed ...$values): int => \array_push($this, ...$values);
            }
            """,
            """
            <?tyhp
            use extension MutArray;
            function f(): void {
                array $a = [3, 1, 2];
                $a->sort();
                $a->push(1);
            }
            """);

        php.Should().Contain(@"\sort($a");
        php.Should().Contain(@"\array_push($a");
        php.Should().NotContain("$a->sort(");
        php.Should().NotContain("$a->push(");
        php.Should().NotContain(@"\MutArray::");
    }

    [Fact]
    public void Emit_SelfKeyword_RewritesToBlockTarget()
    {
        // Block-target `self` emits as the target type's PHP spelling. On
        // `extends int`, `self::class` is `int::class` in the splice and the backer.
        var php = CompileAndEmit("""
            <?tyhp
            extension Boxed extends int {
                function ident(): string {
                    return self::class;
                }
            }
            function f(int $n): string {
                return $n->ident();
            }
            """);

        php.Should().Contain("function f(int $n): string");
        php.Should().Contain("return int::class;");
        php.Should().Contain("function ident(int $this_): string");
        php.Should().NotContain(@"\Boxed::class");
        php.Should().NotContain("self::");
        php.Should().NotContain("$n->ident(");
    }

    [Fact]
    public void Emit_OriginalAst_SetOnReplacement()
    {
        var files = CompileAndEmitFiles("""
            <?tyhp
            extension MathHelpers extends int {
                function doubled(): int {
                    return $this + $this;
                }
            }
            function f(int $n): int {
                return $n->doubled();
            }
            """);

        var stamped = files
            .SelectMany(f => f.Statements.OfType<IBase2Ast>())
            .SelectMany(Flatten)
            .Where(n => n.OriginalAst is not null)
            .ToList();
        stamped.Should().NotBeEmpty("splice replacements must set OriginalAst for sourcemaps");
    }

    [Fact]
    public void AllocateTempName_SkipsOccupied()
    {
        var ctx = new SpliceEngineContext
        {
            ResolveFreeFunction = _ => null,
        };
        ctx.OccupiedVariableNames.Add(GeneratedNames.InlineTempVariablePrefix + "1");
        var name = ctx.AllocateTempName();
        name.Should().Be(GeneratedNames.InlineTempVariablePrefix + "2");
        GeneratedNames.StartsWithInlineTempPrefix(name).Should().BeTrue();
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join('\n', CompileAndEmitFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileAndEmitFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "splice.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            result.Diagnostics.Errors
                .Where(d => d.Code == MessageCode.CheckerInlineParameterMutation)
                .Should().BeEmpty("TYHP4174 must not fire for legitimate &$this splices");

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string CompileAndEmitWithTyhpdef(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "types.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([tyhpdefPath, tyhpPath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            result.Diagnostics.Errors
                .Where(d => d.Code == MessageCode.CheckerInlineParameterMutation)
                .Should().BeEmpty("TYHP4174 must not fire for legitimate &$this splices");

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            return string.Join('\n', new TyhpEmitter(context).Emit(result.ParsedFiles!)
                .Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }

    private static IEnumerable<IBase2Ast> Flatten(IBase2Ast node)
    {
        yield return node;
        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }
}
