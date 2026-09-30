using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class ExtensionBlockTargetEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "extension.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
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
                ["build:optimize"] = "none",
            })
            .Build();
        return new Project(configuration);
    }

    [Fact]
    public void Emit_ObjectTarget_RewritesSelfAndEmitsByValueReceiver()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;

            class Money {
                public const int CODE = 1;
                public function plus(Money $other): Money {
                    return $other;
                }
            }

            extension MoneyOps extends Money {
                function doubled(): self {
                    $code = self::CODE;
                    $copy = new self();
                    return $copy->plus($this);
                }

                function replace(&$this): void {
                    $this = new self();
                }

                operator + (self $left, self $right): self {
                    return $left->plus($right);
                }
            }
            """);

        php.Should().Contain("function doubled(\\App\\Money $this_): \\App\\Money");
        php.Should().NotContain("function doubled(\\App\\Money &$this_)");
        php.Should().Contain("function replace(\\App\\Money &$this_): void");
        php.Should().Contain("\\App\\Money::CODE");
        php.Should().Contain("new \\App\\Money()");
        php.Should().Contain("function __add(\\App\\Money $l, \\App\\Money $r): \\App\\Money");
        php.Should().NotContain("new self");
        php.Should().NotContain("self::");
        php.Should().NotContain("function doubled(self");
        php.Should().NotContain("function __add(self");
    }

    [Fact]
    public void Emit_InterfaceAndEnumTargets_EmitByValueReceiver()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;

            interface Shape {
                public function area(): int;
            }

            enum Color {
                case Red;
            }

            extension ShapeOps extends Shape {
                function label(): string {
                    return 'shape';
                }
            }

            extension ColorOps extends Color {
                function name(): string {
                    return 'Red';
                }
            }
            """);

        php.Should().Contain("function label(\\App\\Shape $this_): string");
        php.Should().Contain("function name(\\App\\Color $this_): string");
        php.Should().NotContain("&$this_");
    }

    [Fact]
    public void Emit_ScalarArrayAndStruct_ByRefOnlyWhenAnnotated()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
            };

            extension StringExtensions extends string {
                function length(): int {
                    return 5;
                }

                function shout(&$this): void {
                    $this = 'X';
                }
            }

            extension ArrayOps extends array {
                function first(): mixed {
                    return $this[0];
                }

                function push(&$this): void {
                    $this[] = 1;
                }
            }

            extension PointOps extends Point {
                function read(): mixed {
                    return $this;
                }

                function bump(&$this): void {
                    $this->x = $this->x + 1;
                }
            }

            function demo(): int {
                return 'hello'->length();
            }
            """);

        php.Should().Contain("function length(string $this_): int");
        php.Should().NotContain("function length(string &$this_)");
        php.Should().Contain("function shout(string &$this_): void");
        php.Should().Contain("function first(array $this_): mixed");
        php.Should().NotContain("function first(array &$this_)");
        php.Should().Contain("function push(array &$this_): void");
        php.Should().Contain("function read(array $this_): mixed");
        php.Should().NotContain("function read(array &$this_)");
        php.Should().Contain("function bump(array &$this_): void");
        php.Should().Contain("return \\StringExtensions::length('hello');");
    }

    [Fact]
    public void Emit_StructFieldWrite_MutatesByRefReceiverArray()
    {
        // Struct field writes (`$this->field = …`) on a `&$this`-annotated struct target are a
        // valid, checker-accepted program: the emitter must alias the struct backer to `$this_`
        // and lower the field access to the array key form (structs are arrays at runtime).
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
            };

            extension PointOps extends Point {
                function bump(&$this): void {
                    $this->x = $this->x + 1;
                }
            }
            """);

        php.Should().Contain("function bump(array &$this_): void");
        php.Should().Contain("$this_['x'] = ($this_['x'] + 1);");
    }

    [Fact]
    public void Emit_ShortFunctionSplice_OmitsBackerAndRewritesSelf()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;

            class Box {
                public int $n = 1;
            }

            extension BoxOps extends Box {
                fn made(): self => new self();
            }

            function demo(): Box {
                $box = new Box();
                return $box->made();
            }
            """);

        php.Should().NotContain("function made");
        php.Should().NotContain("class BoxOps");
        php.Should().Contain("new \\App\\Box()");
        php.Should().NotContain("new self");
        php.Should().NotContain("self::");
    }

    [Fact]
    public void Emit_AliasTarget_ByRefOnlyWhenAnnotated()
    {
        // `type MoneyAlias = Money;` — the block target is written as the alias, but the
        // by-ref receiver decision must still key off the member's own `&$this` annotation,
        // not anything about the alias/resolution path.
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;

            class Money {
                public int $amount = 0;
            }

            type MoneyAlias = Money;

            extension MoneyAliasOps extends MoneyAlias {
                function pureRead(): int {
                    return $this->amount;
                }

                function mutate(&$this): void {
                    $this = new Money();
                }
            }
            """);

        php.Should().Contain("function pureRead(\\App\\Money $this_): int");
        php.Should().NotContain("function pureRead(\\App\\Money &$this_)");
        php.Should().Contain("function mutate(\\App\\Money &$this_): void");
    }

    [Fact]
    public void Emit_NestedTargetGroup_RewritesCallAndRespectsByRefAnnotation()
    {
        // A nested `extends Type { }` group's ContainingScope is the group itself, not the
        // enclosing `extension Name { }`. The call-site rewrite must still find the real
        // (non-group) extension backer to splice/static-call against, and the receiver's
        // by-ref-ness must still come only from that member's own `&$this` annotation.
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;

            extension NumericHelpers {
                extends int {
                    function intLabel(): string {
                        $label = 'int';
                        return $label;
                    }

                    function bump(&$this): void {
                        $this = $this + 1;
                    }
                }
                extends string {
                    function stringLabel(): string {
                        return 'string';
                    }
                }
            }

            function demo(int $n): string {
                return $n->intLabel();
            }
            """);

        php.Should().Contain("function intLabel(int $this_): string");
        php.Should().NotContain("function intLabel(int &$this_)");
        php.Should().Contain("function bump(int &$this_): void");
        php.Should().Contain("function stringLabel(string $this_): string");
        php.Should().Contain(@"\App\NumericHelpers::intLabel($n)");
        php.Should().NotContain("$n->intLabel(");
    }

    [Fact]
    public void Emit_NamedArguments_DoNotIncludeThis()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;

            class Money {
                public int $amount = 0;
            }

            extension MoneyOps extends Money {
                function format(string $currency): string {
                    string $label = $currency;
                    return $label;
                }
            }

            function show(Money $m): string {
                return $m->format(currency: 'USD');
            }
            """);

        php.Should().Contain("function format(\\App\\Money $this_, string $currency): string");
        php.Should().NotContain("function format(\\App\\Money &$this_");
        php.Should().Contain("\\App\\MoneyOps::format(");
        php.Should().Contain("currency:");
        php.Should().NotContain("this:");
        php.Should().NotContain("$this:");
    }
}
