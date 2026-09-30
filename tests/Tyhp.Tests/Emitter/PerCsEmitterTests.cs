using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// PER-CS 3.0 emit contract: unspaced unions/intersections, trailing commas on
/// multiline lists, compact empty bodies, omitted no-arg anonymous <c>()</c>,
/// and FirstExpressionLine control-structure wrapping.
/// </summary>
[Trait("Category", "Emitter")]
public class PerCsEmitterTests
{
    [Fact]
    public void Emit_UnionAndIntersection_HaveNoSpacesAroundSymbols()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;
            interface A {}
            interface B {}
            function take(int|string $x, A&B $y): int|string {
                return $x;
            }
            """);

        php.Should().Contain("function take(int|string $x, \\Probe\\A&\\Probe\\B $y): int|string");
        php.Should().NotContain("int | string");
        php.Should().NotContain("A & ");
    }

    [Fact]
    public void Emit_NullableUnionConstraint_UsesPipeNullNotQuestionPrefix()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function take<T extends int|string>(?T $x): ?T {
                return $x;
            }
            """);

        php.Should().Contain("function take(int|string|null $x): int|string|null");
        php.Should().NotContain("?int|string");
        php.Should().NotContain("?int | string");
    }

    [Fact]
    public void Emit_CatchUnion_HasNoSpacesAroundPipe()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                try {
                    throw new \RuntimeException('x');
                } catch (\RuntimeException|\InvalidArgumentException $e) {
                    echo $e->getMessage();
                }
            }
            """);

        php.Should().Contain("catch (\\RuntimeException|\\InvalidArgumentException $e)");
        php.Should().NotContain("RuntimeException | ");
    }

    [Fact]
    public void Emit_MultilinePromotedParams_HaveTrailingCommaAndCompactCtorBody()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function __construct(
                    public string $name {
                        get => $this->name;
                    },
                ) {}
            }
            """, phpVersion: "8.4");

        php.Should().Contain("public function __construct(");
        php.Should().Contain("public string $name {");
        php.Should().Contain("        },\n    ) {}");
        php.Should().NotContain(") {\n    }");
    }

    [Fact]
    public void Emit_EmptyClassAndMethod_UseCompactBraces()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class EmptyType {}
            class Host {
                public function noop(): void {}
            }
            """).Replace("\r\n", "\n");

        php.Should().Contain("class EmptyType {}");
        php.Should().NotContain("class EmptyType\n{");
        php.Should().Contain("public function noop(): void {}");
        php.Should().NotContain("function noop(): void\n{");
    }

    [Fact]
    public void Emit_NonEmptyClass_KeepsBraceNextLine()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Host {
                public function demo(): int {
                    return 1;
                }
            }
            """).Replace("\r\n", "\n");

        php.Should().Contain("class Host\n{");
        php.Should().Contain("public function demo(): int\n    {");
    }

    [Fact]
    public void Emit_NoArgAnonymousClass_OmitsEmptyParens()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Base {}
            function make(): Base {
                return new class extends Base {
                    public int $x = 0;
                };
            }
            """);

        php.Should().Contain("new class extends Base");
        php.Should().NotContain("new class()");
    }

    [Fact]
    public void Emit_AnonymousClassWithCtorArgs_KeepsParens()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Base {
                public function __construct(int $a, int $b) {}
            }
            function make(): Base {
                return new class(1, 2) extends Base {};
            }
            """);

        php.Should().Contain("new class(1, 2) extends Base");
    }

    [Fact]
    public void Emit_MultilineIfCondition_PutsFirstExpressionOnNextLine()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(int $x): void {
                if (match ($x) {
                    1 => true,
                    default => false,
                }) {
                    echo 'ok';
                }
            }
            """).Replace("\r\n", "\n");

        php.Should().Contain("if (\n        match ($x) {");
        php.Should().NotContain("if (match ($x) {");
    }

    [Fact]
    public void Emit_NamedArgument_HasNoSpaceBeforeColon()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function greet(string $name, int $times = 1): void {}
            function demo(): void {
                greet(name: 'Ada', times: 2);
            }
            """);

        php.Should().Contain("greet(name: 'Ada', times: 2)");
        php.Should().NotContain("name :");
    }

    [Fact]
    public void Emit_ShortClosure_HasNoSpaceAfterFn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $f = fn(int $x): int => $x + 1;
                $f(1);
            }
            """);

        php.Should().Contain("fn(int $x): int =>");
        php.Should().NotContain("fn (int");
    }

    [Fact]
    public void Emit_EmptyClassicClosure_UsesCompactBraces()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $f = function () {};
                $f();
            }
            """).Replace("\r\n", "\n");

        php.Should().Contain("function () {}");
        php.Should().NotContain("function () {\n");
    }

    [Fact]
    public void Emit_PromotedCallableProperty_UsesMixed()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Host {
                public function __construct(public callable $callback) {}
            }
            function take(callable $cb): void {}
            """);

        php.Should().Contain("public mixed $callback");
        php.Should().NotContain("public callable $callback");
        php.Should().Contain("function take(callable $cb): void");
    }

    private static string CompileAndEmit(string content, string phpVersion = "8.2")
    {
        var result = IsolatedCompilation.ParseSnippet(content, phpVersion: phpVersion);

        var unexpectedErrors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        unexpectedErrors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        var context = EmitContext.Create(
            result.GlobalScope,
            result.Diagnostics,
            CreateProject(phpVersion),
            requiresGenericVariant: result.RequiresGenericVariant,
            genericCallTargets: result.GenericCallTargets);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }

    private static Project CreateProject(string phpVersion)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = phpVersion,
            })
            .Build();
        return new Project(configuration);
    }
}
