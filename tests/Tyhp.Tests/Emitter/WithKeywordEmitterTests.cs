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
public class WithKeywordEmitterTests
{
    private static string CompileAndEmit(string tyhp, string phpVersion = "8.4", Project? project = null)
        => string.Join(
            '\n',
            CompileAndEmitFiles(tyhp, phpVersion, project).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileAndEmitFiles(
        string tyhp,
        string phpVersion = "8.4",
        Project? project = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "with.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            project ??= CreateProject(phpVersion);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: phpVersion,
                    configure: o =>
                    {
                        o.Checker = new CheckerOptions
                        {
                            PhpVersion = phpVersion,
                            ExperimentalReadonlyCloneWith = project.Build.ExperimentalReadonlyCloneWith,
                        };
                    }));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!).ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject(string phpVersion, bool experimentalReadonlyCloneWith = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["output:phpVersion"] = phpVersion,
            ["build:experimentalReadonlyCloneWith"] = experimentalReadonlyCloneWith ? "true" : "false",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new Project(configuration);
    }

    [Fact]
    public void Emit_CloneWith_NonReadonly_Php84_Expr_UsesObjectHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): Config {
                return clone $cfg with [enabled => false];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $cfg, ['enabled' => false])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_Php85_UsesNativeCloneCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): Config {
                return clone $cfg with [enabled => false];
            }
            """, phpVersion: "8.5");

        php.Should().Contain("clone($cfg, ['enabled' => false])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_NonReadonly_Stmt_UsesDirectAssignments()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $color = "red";
            }
            function make(): void {
                $x = new Widget() with [color => "blue"];
            }
            """);

        php.Should().Contain("$x = new Widget()");
        php.Should().Contain("$x->color = \"blue\"");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_NonReadonly_Expr_UsesObjectHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $color = "red";
            }
            function make(): Widget {
                return new Widget() with [color => "blue"];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(new Widget(), ['color' => ""blue""])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_InPlaceWith_Stmt_UsesPropertyAssignments()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $color = "red";
            }
            function paint(Widget $w): void {
                $w with [color => "green"];
            }
            """);

        php.Should().Contain("$w->color = \"green\"");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NestedWith_InnerExprOuterStmt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Inner {
                public bool $b = true;
            }
            class MyClass {
                public Inner $a;
            }
            function demo(Inner $c): void {
                $b = new MyClass() with [
                    a => clone $c with [b => false]
                ];
            }
            """);

        php.Should().Contain("$b = new MyClass()");
        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $c, ['b' => false])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_StructWith_StillUsesArrayReplace()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            function move(Point $p): array {
                return $p with [x => 1];
            }
            """);

        php.Should().Contain(@"\array_replace($p, ['x' => 1])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_AssignmentStmt_Php84_UsesDirectAssignments()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): void {
                $a = clone $cfg with [enabled => false];
            }
            """);

        php.Should().Contain("$a = clone $cfg");
        php.Should().Contain("$a->enabled = false");
        php.Should().NotContain("ObjectHelper");
    }

    [Fact]
    public void Emit_CloneWith_Readonly_Php84_Experimental_UsesReflectionIife()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha;

                public function __construct() {
                    $this->alpha = 255;
                }
            }
            function copyColor(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """,
            phpVersion: "8.4",
            project: CreateProject("8.4", experimentalReadonlyCloneWith: true));

        php.Should().Contain("newInstanceWithoutConstructor");
        php.Should().Contain("ReflectionObject");
        php.Should().Contain("public static array $__tyhp_overrides");
        php.Should().Contain("public function __construct()\n");
        php.Should().NotContain("__construct(): void");
        php.Should().Contain("setStaticPropertyValue");
        php.Should().Contain("['alpha' => 128]");
        php.Should().NotContain("public array $__tyhp_overrides");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_Readonly_RequiredConstructor_Php83_SkipsParentConstructor()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box {
                public function __construct(public readonly int $w, public readonly int $h) {}
            }
            function copyBox(Box $b): Box {
                return clone $b with [w => 7];
            }
            """,
            phpVersion: "8.3",
            project: CreateProject("8.3", experimentalReadonlyCloneWith: true));

        php.Should().Contain("new \\ReflectionClass(new class extends \\Box");
        php.Should().Contain("public function __construct()\n");
        php.Should().NotContain("__construct(): void");
        php.Should().Contain("public static array $__tyhp_overrides");
        php.Should().Contain("newInstanceWithoutConstructor");
        php.Should().Contain("})($b, ['w' => 7])");
        php.Should().NotContain("public array $__tyhp_overrides");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_Readonly_Php83_UsesAnonymousCloneWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha;

                public function __construct() {
                    $this->alpha = 255;
                }
            }
            function make(): Color {
                return new Color() with [alpha => 128];
            }
            """, phpVersion: "8.3");

        php.Should().Contain("clone (new class(");
        php.Should().Contain("extends \\Color");
        php.Should().Contain("function __clone(): void");
        php.Should().Contain("$this->alpha = 128");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain("new readonly class");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_ReadonlyClass_Php83_UsesReadonlyAnonymousWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            readonly class Color {
                public function __construct(public int $alpha) {}
            }
            function make(): Color {
                return new Color(255) with [alpha => 128];
            }
            """, phpVersion: "8.3");

        php.Should().Contain("clone (new readonly class(255) extends \\Color");
        php.Should().Contain("$this->alpha = 128");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain("public array $__tyhp_overrides");
    }

    [Fact]
    public void Emit_NewWith_Readonly_Php84_UsesAnonymousCloneWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha;

                public function __construct() {
                    $this->alpha = 255;
                }
            }
            function make(): Color {
                return new Color() with [alpha => 128];
            }
            """);

        php.Should().Contain("clone (new class(");
        php.Should().Contain("extends \\Color");
        php.Should().Contain("function __clone(): void");
        php.Should().Contain("$this->alpha = 128");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_ReadonlyClassImplicitProperty_Php84_UsesReadonlyAnonymousWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            readonly class Color {
                public function __construct(public int $alpha) {}
            }
            function make(): Color {
                return new Color(255) with [alpha => 128];
            }
            """);

        php.Should().Contain("clone (new readonly class(255) extends \\Color");
        php.Should().Contain("$this->alpha = 128");
        php.Should().NotContain("ObjectHelper");
    }

    [Fact]
    public void Emit_CloneWith_ReadonlyClassImplicitProperty_Php85_UsesNativeCloneCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            readonly class Color {
                public function __construct(public int $alpha) {}
            }
            function fade(Color $color): Color {
                return clone $color with [alpha => 128];
            }
            """, phpVersion: "8.5");

        php.Should().Contain("clone($color, ['alpha' => 128])");
        php.Should().NotContain("ObjectHelper");
    }

    [Fact]
    public void Emit_NewWith_Readonly_Php85_UsesNativeCloneCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha;

                public function __construct() {
                    $this->alpha = 255;
                }
            }
            function make(): Color {
                return new Color() with [alpha => 128];
            }
            """, phpVersion: "8.5");

        php.Should().Contain("clone(new Color(), ['alpha' => 128])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain("new class");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_TrailingComma_DoesNotEmitEmptyArrayElement()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): Config {
                return clone $cfg with [enabled => false,];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $cfg, ['enabled' => false])");
        php.Should().NotMatchRegex(@",\s*,");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_NestedArrayTrailingComma_DoesNotEmitEmptyArrayElement()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public array $items = [];
            }
            function copyConfig(Config $cfg): Config {
                return clone $cfg with [items => [1, 2,]];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $cfg, ['items' => [1, 2]])");
        php.Should().NotMatchRegex(@",\s*,");
    }

    [Fact]
    public void Emit_CloneWith_Readonly_NestedArrayTrailingComma_DoesNotEmitEmptyArrayElement()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly array $channels;

                public function __construct() {
                    $this->channels = [];
                }
            }
            function copyColor(Color $c): Color {
                return clone $c with [channels => [1, 2,]];
            }
            """,
            phpVersion: "8.4",
            project: CreateProject("8.4", experimentalReadonlyCloneWith: true));

        php.Should().Contain("['channels' => [1, 2]]");
        php.Should().NotMatchRegex(@",\s*,");
        php.Should().NotContain(" with ");
    }

    [Theory]
    [InlineData("8.3")]
    [InlineData("8.4")]
    public void Run_CloneWith_Readonly_RequiredConstructor_Experimental_CopiesWithoutParentConstructor(
        string phpVersion)
    {
        // `Box::__construct` has required parameters. The wrapper must not run it with no
        // arguments, must not put override state on the instance, and must pop its override
        // frame even when the same call site re-enters through recursion.
        var files = CompileAndEmitFiles("""
            <?tyhp
            class Box {
                public function __construct(public readonly int $w, public readonly int $h) {}
            }

            function copyBox(Box $b): Box {
                return clone $b with [w => 7];
            }

            function widen(Box $b, int $n): Box {
                if ($n === 0) {
                    return $b;
                }

                return clone $b with [w => widen($b, $n - 1)->w + 10];
            }
            """,
            phpVersion: phpVersion,
            project: CreateProject(phpVersion, experimentalReadonlyCloneWith: true));

        var output = EmittedPhpRunner.Run(
            files,
            """
            $b = new \Box(1, 2);
            $c = \copyBox($b);
            echo $c->w, ',', $c->h, ',', $b->w, ',', $b->h, "\n";
            echo \count(\get_object_vars($c)), "\n";
            $d = \copyBox($c);
            echo $d->w, ',', $d->h, "\n";
            echo \widen($b, 2)->w, ',', $b->w, "\n";
            """);

        output.Should().Be("7,2,1,2\n2\n7,2\n21,1\n");
    }

    [Theory]
    [InlineData("8.3")]
    [InlineData("8.4")]
    public void Run_NewWith_Readonly_AnonymousCloneWrapper_AppliesOverride(string phpVersion)
    {
        var files = CompileAndEmitFiles("""
            <?tyhp
            class Pair {
                public function __construct(public readonly int $a, public readonly int $b) {}
            }

            readonly class Tone {
                public function __construct(public int $alpha, public int $beta) {}
            }

            function makePair(): Pair {
                return new Pair(1, 2) with [a => 9];
            }

            function makeTone(): Tone {
                return new Tone(255, 3) with [alpha => 128];
            }
            """,
            phpVersion: phpVersion);

        var output = EmittedPhpRunner.Run(
            files,
            """
            $p = \makePair();
            $t = \makeTone();
            echo $p->a, ',', $p->b, ',', $t->alpha, ',', $t->beta, "\n";
            """);

        output.Should().Be("9,2,128,3\n");
    }

    [Fact]
    public void Emit_ObjectWithSpread_Php84_UsesObjectHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Point {
                public int $x = 0;
                public int $y = 0;
            }

            type Src = struct {
                int $x = 0;
                int $y = 0;
            };

            function copy(Point $p, Src $src): Point {
                return clone $p with [...$src, x => 1];
            }

            function make(Src $src): Point {
                return new Point() with [...$src];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $p, [...$src, 'x' => 1])");
        php.Should().Contain(@"\Tyhp\ObjectHelper::with(new Point(), [...$src])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_ObjectWithSpread_Php85_UsesNativeClone()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Point {
                public int $x = 0;
                public int $y = 0;
            }

            type Src = struct {
                int $x = 0;
                int $y = 0;
            };

            function copy(Point $p, Src $src): Point {
                return clone $p with [...$src, x => 1];
            }
            """, phpVersion: "8.5");

        php.Should().Contain("clone($p, [...$src, 'x' => 1])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }
}
