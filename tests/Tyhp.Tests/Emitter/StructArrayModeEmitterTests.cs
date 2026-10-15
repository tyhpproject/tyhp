using System;
using System.Linq;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Array-backed struct emission: in-place <c>with</c> statements, anonymous struct types, and
/// nested struct defaults.
/// </summary>
[Trait("Category", "Emitter")]
public class StructArrayModeEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "structs.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project: null,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void InPlaceWith_TopLevelStatement_ReassignsVariable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x;
                int $y;
            };
            Point $p = new Point() with [x => 1, y => 2];
            $p with [x => 5];
            echo $p->x;
            """);

        php.Should().Contain(@"$p = \array_replace($p, ['x' => 5]);");
        php.Should().NotMatchRegex(@"(?m)^\s*\\array_replace\(");
    }

    [Fact]
    public void InPlaceWith_StatementInFunction_ReassignsVariable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x;
                int $y;
            };
            function bump(): Point {
                Point $p = new Point() with [x => 1, y => 2];
                $p with [x => 5];
                return $p;
            }
            """);

        php.Should().Contain(@"$p = \array_replace($p, ['x' => 5]);");
        php.Should().NotMatchRegex(@"(?m)^\s*\\array_replace\(");
    }

    [Fact]
    public void InPlaceWith_StatementOnProperty_ReassignsProperty()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x;
                int $y;
            };
            class Holder {
                public Point $pos;

                public function __construct() {
                    $this->pos = new Point() with [x => 1, y => 2];
                }

                public function move(): void {
                    $this->pos with [x => 50];
                }
            }
            """);

        php.Should().Contain(@"$this->pos = \array_replace($this->pos, ['x' => 50]);");
        php.Should().NotMatchRegex(@"(?m)^\s*\\array_replace\(");
    }

    [Fact]
    public void InPlaceWith_StatementWithAliasedKey_ResolvesAlias()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Row = struct {
                string 'String Value' as $strVal = "";
                int $n = 0;
            };
            Row $r = new Row();
            $r with [strVal => "x"];
            echo $r->strVal;
            """);

        php.Should().Contain(@"$r = \array_replace($r, ['String Value' => ""x""]);");
        php.Should().Contain("$r['String Value']");
    }

    [Fact]
    public void InPlaceWith_StatementOnAnonymousStruct_ReassignsVariable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function coords(): struct { float $lat; float $lng; } {
                return new struct { float $lat; float $lng; } with [lat => 1.0, lng => 2.0];
            }
            $c = coords();
            $c with [lng => 9.0];
            echo $c->lng;
            """);

        php.Should().Contain(@"$c = \array_replace($c, ['lng' => 9.0]);");
        php.Should().Contain("$c['lng']");
        php.Should().NotContain("$c->lng");
    }

    [Fact]
    public void AnonymousStruct_ReturnedValue_PropertyReadUsesArrayAccess()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function coords(): struct { float $lat; float $lng; } {
                return new struct { float $lat; float $lng; } with [lat => 40.5, lng => -74.0];
            }
            $c = coords();
            echo $c->lat, " ", $c->lng;
            """);

        php.Should().Contain("$c['lat']");
        php.Should().Contain("$c['lng']");
        php.Should().NotContain("$c->lat");
        php.Should().NotContain("$c->lng");
    }

    [Fact]
    public void AnonymousStruct_Parameter_PropertyReadUsesArrayAccess()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function show(struct { string $name; int $age; } $person): string {
                return $person->name . ":" . $person->age;
            }
            """);

        php.Should().Contain("$person['name']");
        php.Should().Contain("$person['age']");
        php.Should().NotContain("$person->name");
    }

    [Fact]
    public void AnonymousStruct_PropertyWrite_UsesArrayAccess()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function coords(): struct { float $lat; float $lng; } {
                return new struct { float $lat; float $lng; } with [lat => 40.5, lng => -74.0];
            }
            $c = coords();
            $c->lat = 1.5;
            """);

        php.Should().Contain("$c['lat'] = 1.5;");
        php.Should().NotContain("$c->lat");
    }

    [Fact]
    public void AnonymousStruct_CloneWith_EmitsArrayReplace()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function coords(): struct { float $lat; float $lng; } {
                return new struct { float $lat; float $lng; } with [lat => 40.5, lng => -74.0];
            }
            $c = coords();
            $c2 = clone $c with [lng => 9.0];
            echo $c2->lng;
            """);

        php.Should().Contain(@"$c2 = \array_replace($c, ['lng' => 9.0]);");
        php.Should().Contain("$c2['lng']");
        php.Should().NotContain("->lng");
    }

    [Fact]
    public void AnonymousStruct_IssetAndCoalesce_UseArrayAccess()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function coords(): struct { float $lat; ?float $lng; } {
                return new struct { float $lat; ?float $lng; } with [lat => 40.5];
            }
            $c = coords();
            bool $has = isset($c->lng);
            float $v = $c->lng ?? 0.0;
            """);

        php.Should().Contain("isset($c['lng'])");
        php.Should().Contain("$c['lng'] ?? 0.0");
        php.Should().NotContain("$c->lng");
    }

    [Fact]
    public void AnonymousStruct_StringAlias_UsesAliasKey()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function make(): struct { string 'Reply-To' as $replyTo; } {
                return new struct { string 'Reply-To' as $replyTo; } with [replyTo => "a@b.c"];
            }
            $m = make();
            echo $m->replyTo;
            """);

        php.Should().Contain("$m['Reply-To']");
        php.Should().NotContain("$m->replyTo");
    }

    [Fact]
    public void NestedStructDefault_FoldsInnerDefaultsInsteadOfNewInner()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Inner = struct {
                int $a = 1;
                string $b = "z";
            };
            type Outer = struct {
                Inner $inner = new Inner();
                int $n = 3;
            };
            $o = new Outer();
            echo $o->inner->a;
            """);

        php.Should().Contain("$o = ['inner' => ['a' => 1, 'b' => \"z\"], 'n' => 3];");
        php.Should().NotContain("new Inner");
    }

    [Fact]
    public void NestedStructDefault_WithOverrides_FoldsInnerDefaults()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Inner = struct {
                int $a = 1;
                string $b = "z";
            };
            type Outer = struct {
                Inner $inner = new Inner() with [a => 7];
                int $n = 3;
            };
            $o = new Outer() with [n => 4];
            """);

        php.Should().Contain("$o = ['inner' => ['a' => 7, 'b' => \"z\"], 'n' => 4];");
        php.Should().NotContain("new Inner");
    }
}
