using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.7 Phase 1: <c>list()</c> / <c>[]</c> destructure on <c>ArrayAccess</c>
/// (homogeneous <c>TValue</c> and <c>ArrayAccessShape</c> per-key). TYHP4094 when the
/// source is not array, string, or ArrayAccess. Spread remains TYHP4095.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.7")]
public class ArrayAccessDestructureTests
{
    private const string ConfigMap = """
        type ConfigMap = struct {
            string $host = '';
            int $port = 0;
        };
        """;

    [Fact]
    public void Check_PositionalHomogeneous_BindsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, User> $o): User {
                [$a, $b] = $o;
                return $a;
            }
            """);

        errors.Should().BeEmpty(
            "ArrayAccess<int, User> positional destructure should bind User: " + Describe(errors));
    }

    [Fact]
    public void Check_ListKeywordHomogeneous_BindsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, User> $o): User {
                list($a, $b) = $o;
                return $b;
            }
            """);

        errors.Should().BeEmpty(
            "list() on ArrayAccess<int, User> should bind User: " + Describe(errors));
    }

    [Fact]
    public void Check_NamedHomogeneous_BindsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function read(\ArrayAccess<string, bool> $o): bool {
                ['asdf' => $x, 'myFlag' => $y] = $o;
                return $x;
            }
            """);

        errors.Should().BeEmpty(
            "named destructure on ArrayAccess<string, bool> should bind bool: "
            + Describe(errors));
    }

    [Fact]
    public void Check_ShapeNamedHost_BindsFieldType()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            function read(\Tyhp\Contracts\ArrayAccessShape<ConfigMap> $o): string {
                ['host' => $h] = $o;
                return $h;
            }
            """);

        errors.Should().BeEmpty(
            "ArrayAccessShape named host should bind string: " + Describe(errors));
    }

    [Fact]
    public void Check_ShapeWideKey_Reports4331()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            function read(\Tyhp\Contracts\ArrayAccessShape<ConfigMap> $o, string $s): mixed {
                [$s => $h] = $o;
                return $h;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeWideKey,
            "wide string key in shape destructure must emit 4331: " + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "wide key should not also be a generic type mismatch: " + Describe(errors));
    }

    [Fact]
    public void Check_SkippedSlot_DoesNotRequireKeyZero()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, User> $o): User {
                [, $b] = $o;
                return $b;
            }
            """);

        errors.Should().BeEmpty(
            "[, $b] on ArrayAccess<int, User> should bind $b as User without key 0: "
            + Describe(errors));
    }

    [Fact]
    public void Check_NestedArrayAccess_BindsInnerTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, \ArrayAccess<int, User>> $o): User {
                [[$a, $b], $c] = $o;
                return $a;
            }
            """);

        errors.Should().BeEmpty(
            "nested destructure when offsetGet(0) is ArrayAccess should bind User: "
            + Describe(errors));
    }

    [Fact]
    public void Check_NestedArrayValue_BindsElement()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, array<int, User>> $o): User {
                [[$a, $b], $c] = $o;
                return $a;
            }
            """);

        errors.Should().BeEmpty(
            "nested destructure when offsetGet(0) is array should bind User: "
            + Describe(errors));
    }

    [Fact]
    public void Check_PlainClass_Reports4094()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Plain {}
            function bad(Plain $p): mixed {
                [$a] = $p;
                return $a;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerDestructuringNonArray,
            "plain class destructure must emit 4094: " + Describe(errors));
    }

    [Fact]
    public void Check_SpreadInDestructure_Reports4095()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function bad(array<int> $xs): mixed {
                [...$rest] = $xs;
                return $rest;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerDestructuringSpread,
            "spread in destructure must emit 4095: " + Describe(errors));
    }

    [Fact]
    public void Check_PositionalKeyVsStringTKey_ReportsTypeMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function read(\ArrayAccess<string, bool> $o): bool {
                [$a] = $o;
                return $a;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "positional int key against ArrayAccess<string, bool> TKey: " + Describe(errors));
    }

    [Fact]
    public void Check_ForeachDestructureArrayAccessElement_BindsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(array<int, \ArrayAccess<int, User>> $rows): ?User {
                foreach ($rows as [$a, $b]) {
                    return $a;
                }
                return null;
            }
            """);

        errors.Should().BeEmpty(
            "foreach ($rows as [$a, $b]) on an ArrayAccess element should bind $a as User: "
            + Describe(errors));
    }

    [Fact]
    public void Check_ForeachDestructureArrayAccessElement_NamedKeys_BindsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function read(array<int, \ArrayAccess<string, bool>> $rows): bool {
                foreach ($rows as ['asdf' => $x]) {
                    return $x;
                }
                return false;
            }
            """);

        errors.Should().BeEmpty(
            "foreach ($rows as ['asdf' => $x]) on an ArrayAccess element should bind bool: "
            + Describe(errors));
    }

    [Fact]
    public void Check_ArraySource_StillAllowed()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(array<int, User> $xs): User {
                [$a, $b] = $xs;
                return $a;
            }
            """);

        errors.Should().BeEmpty(
            "array destructure should still bind the element type: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessStructTKey_DestructureReports4330Once()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Config = struct {
                string $host = '';
            };
            function f(\ArrayAccess<Config, mixed> $o): void {
                [$x] = $o;
            }
            """);

        errors.Should().ContainSingle(
            e => e.Code == MessageCode.CheckerArrayAccessKeyNotOffset,
            "destructuring an ArrayAccess with a struct TKey must not emit 4330 at both the "
            + "parameter annotation and the destructure site: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code} @{e.Line}:{e.Column}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull(
                "bind should succeed: " + Describe(result.Diagnostics.Errors.ToList()));
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
