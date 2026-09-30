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
public class StructEmitterTests
{
    private static string CompileAndEmit(string tyhp, Project? project = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "structs.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            // Ignore .tyhpdef infrastructure noise; struct→array assignability is covered by
            // GradualArrayAssignabilityTests / TypeComparerTests.
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

    private static (string Php, DiagnosticBag Diagnostics) CompileAndEmitWithDiagnostics(
        string tyhp,
        Project project)
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

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
            return (php, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_StructNew_EmitsAssociativeArrayWithDefaults()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
                ?string $label;
            };
            function make(): array {
                return new Point();
            }
            """);

        php.Should().Contain("['x' => 0, 'y' => 0]");
        php.Should().NotContain("new Point");
        php.Should().NotContain("struct Point");
        php.Should().NotContain("'label'");
    }

    [Fact]
    public void Emit_ClassMemberStructNewWith_EmitsArrayNotObjectHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
            }
            function origin(): C\Point {
                return new C\Point() with [x => 1.0, y => 2.0];
            }
            """);

        php.Should().Contain("['x' => 1.0, 'y' => 2.0]");
        php.Should().NotContain("new \\C\\Point");
        php.Should().NotContain("new C\\Point");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
        php.Should().NotContain("function Point(");
        php.Should().NotContain("C::Point()");
    }

    [Fact]
    public void Emit_TwoClassesWithSameNamedMemberStruct_DoNotCollide()
    {
        // Two unrelated classes each declare their own nested `Point`; the FQNs (`\A\Point` /
        // `\B\Point`) must stay distinct so each `new …\Point() with […]` builds its own fields
        // instead of aliasing the other class's struct.
        var php = CompileAndEmit("""
            <?tyhp
            class A {
                type Point = struct {
                    int $x = 0;
                };
            }
            class B {
                type Point = struct {
                    string $label = "";
                };
            }
            function makeA(): A\Point {
                return new A\Point() with [x => 1];
            }
            function makeB(): B\Point {
                return new B\Point() with [label => "hi"];
            }
            """);

        php.Should().Contain("['x' => 1]");
        php.Should().Contain("['label' => \"hi\"]");
        php.Should().NotContain("ObjectHelper");
    }

    [Fact]
    public void Emit_DerivedStructNew_IncludesInheritedDefaults()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type SerializedExpression = struct {
                string $nodeType = '';
                string $type = '';
            };
            type SerializedParameterExpression = struct extends SerializedExpression  {
                string $name = '';
                int $index = 0;
            };
            function make(): array {
                return new SerializedParameterExpression();
            }
            """);

        php.Should().Contain("['nodeType' => '', 'type' => '', 'name' => '', 'index' => 0]");
    }

    [Fact]
    public void Emit_DerivedStructWith_OverridesInheritedAndQuotedKeys()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type SerializedExpression = struct {
                string $nodeType = '';
                string $type = '';
            };
            type SerializedParameterExpression = struct extends SerializedExpression  {
                string $name = '';
            };
            function make(): array {
                return new SerializedParameterExpression() with [
                    nodeType => 'parameter',
                    'type' => 'int',
                    name => 'id',
                ];
            }
            """);

        php.Should().Contain("'nodeType' => 'parameter'");
        php.Should().Contain("'type' => 'int'");
        php.Should().Contain("'name' => 'id'");
        php.Should().NotContain(@"\array_replace(");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_StructPropertyAccess_UsesArrayKey()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            function read(Point $p): int {
                return $p->x;
            }
            """);

        php.Should().Contain("$p['x']");
        php.Should().NotContain("$p->x");
        php.Should().Contain("function read(array $p)");
    }

    [Fact]
    public void Emit_ForeachArrayOfStruct_PropertyAccessUsesArrayKey()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function sum(array<Money> $amounts): int {
                int $total = 0;
                foreach ($amounts as $m) {
                    $total += $m->cents;
                }
                return $total;
            }
            """);

        php.Should().Contain("$m['cents']");
        php.Should().NotContain("$m->cents");
    }

    [Fact]
    public void Emit_ForeachIterableOfStruct_PropertyAccessUsesArrayKey()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function sum(iterable<Money> $amounts): int {
                int $total = 0;
                foreach ($amounts as $m) {
                    $total += $m->cents;
                }
                return $total;
            }
            """);

        php.Should().Contain("$m['cents']");
        php.Should().NotContain("$m->cents");
    }

    [Fact]
    public void Emit_InferredStructVariable_PropertyAccessUsesArrayKey()
    {
        // Same pattern as Story 11 manual verification: `$p = new Point()` has no declared
        // type, but `$p->x` / aliased `$item->displayName` must still rewrite to array keys.
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            type NamedItem = struct {
                string 'Display Name' as $displayName = "";
                int $id = 0;
            };
            function main(): int {
                $p = new Point();
                $p->x = 10;
                $val = $p->y;

                $item = new NamedItem();
                $item->displayName = "Hello";
                return 0;
            }
            """);

        php.Should().Contain("$p['x'] = 10");
        php.Should().Contain("$val = $p['y']");
        php.Should().Contain("$item['Display Name'] = \"Hello\"");
        php.Should().NotContain("$p->x");
        php.Should().NotContain("$p->y");
        php.Should().NotContain("$item->displayName");
    }

    [Fact]
    public void Emit_StructAliasedProperty_UsesAliasStringAsArrayKey()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Row = struct {
                string 'String Value 1243***' as $strVal = "";
            };
            function make(): array {
                return new Row();
            }
            function read(Row $r): string {
                return $r->strVal;
            }
            """);

        php.Should().Contain("['String Value 1243***' => \"\"]");
        php.Should().Contain("$r['String Value 1243***']");
        php.Should().NotContain("$r->strVal");
    }

    [Fact]
    public void Emit_StructNumericAliasedProperty_UsesIntegerArrayKey()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type NumericArgs1 = struct {
                mixed 0 as $arg1 = null;
            };
            type NumericArgs2 = struct extends NumericArgs1  {
                ?mixed 1 as $arg2 = null;
            };
            function makeNumericArgs(): array {
                return new NumericArgs2() with [
                    arg1 => "first",
                    arg2 => 2,
                ];
            }
            function readNumericArgs(NumericArgs2 $args): mixed {
                return $args->arg1;
            }
            """);

        php.Should().Contain("0 =>");
        php.Should().Contain("1 =>");
        php.Should().Contain("$args[0]");
        php.Should().NotContain("$args['0']");
        php.Should().NotContain("$args->arg1");
        php.Should().NotContain("'0' =>");
        php.Should().NotContain("'1' =>");
    }

    [Fact]
    public void Emit_GenericStruct_ErasesTypeArgsToArray()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Box<T> = struct {
                T $value;
            };
            function makeBox(): array {
                return new Box<int>() with [value => 1];
            }
            function readBox(Box<int> $box): int {
                return $box->value;
            }
            """);

        php.Should().NotContain("Box<");
        php.Should().NotContain("tyhpGeneric");
        php.Should().NotContain("GenericObject");
        php.Should().Contain("function makeBox(): array");
        php.Should().Contain("function readBox(array $box): int");
        php.Should().Contain("$box['value']");
        php.Should().NotContain("$box->value");
    }

    [Fact]
    public void Emit_GenericStructExtends_NumericAliases()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type NumericGenericArgs1<T1> = struct {
                T1 0 as $_1;
            };
            type NumericGenericArgs2<T1, T2> = struct extends NumericGenericArgs1<T1> {
                T2 1 as $_2;
            };
            function makeArgs(): array {
                return new NumericGenericArgs2<string, int>() with [
                    _1 => "hello",
                    _2 => 42,
                ];
            }
            """);

        php.Should().Contain("0 =>");
        php.Should().Contain("1 =>");
        php.Should().NotContain("NumericGenericArgs");
        php.Should().NotContain("tyhpGeneric");
    }

    [Fact]
    public void Emit_StructClone_IsNoOp()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
            };
            function copyPoint(Point $p): array {
                return clone $p;
            }
            """);

        php.Should().Contain("return $p;");
        php.Should().NotContain("clone $p");
    }

    [Fact]
    public void Emit_AnonymousStructNew_EmitsDefaultsArray()
    {
        // CHECKER_GAPS P1 #16: anonymous `new struct {…}` binds and emits like a named struct.
        var php = CompileAndEmit("""
            <?tyhp
            function make(): array {
                return new struct {
                    int $x = 1;
                    string $label = "pt";
                };
            }
            """);

        php.Should().Contain("['x' => 1, 'label' => \"pt\"]");
        php.Should().NotContain("anonStruct@");
        php.Should().NotContain("new struct");
    }

    [Fact]
    public void Emit_StructInPlaceWith_UsesArrayReplace()
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
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_StructCloneWith_UsesArrayReplace()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            function move(Point $p): array {
                return clone $p with [y => 2];
            }
            """);

        php.Should().Contain(@"\array_replace($p, ['y' => 2])");
        php.Should().NotContain("clone $p");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_StructNewWith_MergesDefaultsIntoSingleArrayLiteral()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            function make(): array {
                return new Point() with [x => 1];
            }
            """);

        php.Should().Contain("['x' => 1, 'y' => 0]");
        php.Should().NotContain(@"\array_replace(");
        php.Should().NotContain("new Point");
    }

    [Fact]
    public void Emit_CustomStructBacking_EmitsBackingClassAndReportsMissingClass()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["build:structBacking"] = @"App\MissingStructBacking",
            })
            .Build();
        var project = new Project(configuration);

        var (php, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            type Point = struct {
                int $x = 0;
            };
            function make(): Point {
                return new Point();
            }
            """, project);

        php.Should().Contain(@"new \App\MissingStructBacking(['x' => 0])");
        php.Should().Contain(@"function make(): \App\MissingStructBacking");
        diagnostics.Errors.Should().Contain(e => e.Code == MessageCode.EmitterStructBackingError);
    }

    [Fact]
    public void Emit_StructWithAliasedKey_ResolvesAliasInOverrideList()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Row = struct {
                string 'String Value 1243***' as $strVal = "";
            };
            function renameRow(Row $r): array {
                return $r with [strVal => "new"];
            }
            """);

        php.Should().Contain(@"\array_replace($r, ['String Value 1243***' => ""new""])");
        php.Should().NotContain("'strVal'");
    }

    [Fact]
    public void Emit_SameVariableNameAcrossFunctions_DoesNotBleedStructRewrite()
    {
        // `$p` is a struct in `usesStruct` but an ordinary object in `usesObject`. The struct
        // array-access rewrite must stay scoped to its function and not touch `$p->name` elsewhere.
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $name = "";
            }
            type Point = struct {
                int $x = 0;
            };
            function usesStruct(Point $p): int {
                return $p->x;
            }
            function usesObject(Widget $p): string {
                return $p->name;
            }
            """);

        php.Should().Contain("$p['x']");
        php.Should().Contain("$p->name");
        php.Should().NotContain("$p['name']");
    }

    [Fact]
    public void Emit_NestedStructPropertyAccess_ChainsArrayKeys()
    {
        // FOUND_BUGS #44/#45 review: the first `->` hop erases to `$o['inner']`, a freshly
        // created node with no checked type recorded. Resolving the second hop's struct type
        // must not silently fall back to the outer struct (`Outer` has no `x`, which used to
        // leave `->x` un-rewritten on an array — silent wrong emit).
        var php = CompileAndEmit("""
            <?tyhp
            type Inner = struct {
                int $x = 0;
            };
            type Outer = struct {
                Inner $inner;
            };
            function read(Outer $o): int {
                return $o->inner->x;
            }
            """);

        php.Should().Contain("$o['inner']['x']");
        php.Should().NotContain("->x");
        php.Should().NotContain("->inner");
    }

    [Fact]
    public void Emit_ForeachArrayOfStruct_NestedStructPropertyAccessChainsArrayKeys()
    {
        // Same nested-chain hazard as above, but the outer variable's struct type comes from
        // the foreach-value registration (FOUND_BUGS #44) rather than a parameter annotation.
        var php = CompileAndEmit("""
            <?tyhp
            type Inner = struct {
                int $x = 0;
            };
            type Outer = struct {
                Inner $inner;
            };
            function readAll(array<Outer> $items): int {
                int $total = 0;
                foreach ($items as $o) {
                    $total += $o->inner->x;
                }
                return $total;
            }
            """);

        php.Should().Contain("$o['inner']['x']");
        php.Should().NotContain("->x");
        php.Should().NotContain("->inner");
    }

    [Fact]
    public void Emit_StructNewWith_TrailingComma_DoesNotEmitEmptyArrayElement()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            function make(): array {
                return new Point() with [x => 1,];
            }
            """);

        php.Should().Contain("['x' => 1, 'y' => 0]");
        php.Should().NotMatchRegex(@",\s*,");
        php.Should().NotContain(@"\array_replace(");
    }

    [Fact]
    public void Emit_StructInPlaceWith_TrailingComma_DoesNotEmitEmptyArrayElement()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                int $y = 0;
            };
            function move(Point $p): array {
                return $p with [x => 1,];
            }
            """);

        php.Should().Contain(@"\array_replace($p, ['x' => 1])");
        php.Should().NotMatchRegex(@",\s*,");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_StructFieldDefaultArray_TrailingComma_DoesNotEmitEmptyArrayElement()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Box = struct {
                array $items = [1, 2,];
                int $count = 0;
            };
            function make(): array {
                return new Box() with [count => 1,];
            }
            """);

        php.Should().Contain("['items' => [1, 2], 'count' => 1]");
        php.Should().NotMatchRegex(@",\s*,");
    }

    [Fact]
    public void EmitContext_IsStructBackedByArray_DefaultsTrueWhenProjectNull()
    {
        var context = EmitContext.Create(null, new DiagnosticBag(), project: null);
        context.IsStructBackedByArray().Should().BeTrue();
        context.GetStructBacking().Should().Be("array");
    }
}
