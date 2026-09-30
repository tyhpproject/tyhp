using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 6c: homogeneous <c>$obj[$k]</c> via <c>ArrayAccess&lt;TKey, TValue&gt;</c>,
/// TYHP4093 for non-indexable receivers, and TYHP4330 when <c>TKey</c> is a struct or array.
/// </summary>
[Trait("Category", "Checker")]
public class ArrayAccessIndexingTests
{
    [Fact]
    public void Check_ArrayAccessIndex_IsTValueWhenKeyMatchesTKey()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<string, User> $o, string $k): User {
                return $o[$k];
            }
            """);

        errors.Should().BeEmpty(
            "ArrayAccess<string, User> index should be User: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessAssign_AcceptsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function write(\ArrayAccess<string, User> $o, string $k, User $u): void {
                $o[$k] = $u;
            }
            """);

        errors.Should().BeEmpty(
            "$obj[$k] = TValue should typecheck: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessAppend_AcceptsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function append(\ArrayAccess<string, User> $o, User $u): void {
                $o[] = $u;
            }
            """);

        errors.Should().BeEmpty(
            "$obj[] = TValue should typecheck via offsetSet(null, …): " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessWrongKeyType_ReportsTypeMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<string, User> $o, int $k): User {
                return $o[$k];
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "int index against ArrayAccess<string, User> TKey: " + Describe(errors));
    }

    [Fact]
    public void Check_PlainClassIndex_Reports4093()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Plain {}
            function bad(Plain $p): mixed {
                return $p[0];
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerInvalidArrayAccess,
            "plain class index must emit 4093: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessStructTKey_Reports4330()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Config = struct {
                string $host = '';
            };
            function f(\ArrayAccess<Config, mixed> $o): void {}
            """);

        errors.Should().ContainSingle(
            e => e.Code == MessageCode.CheckerArrayAccessKeyNotOffset,
            "annotation-only struct TKey must emit 4330 once: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessArrayTKey_Reports4330()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function f(\ArrayAccess<array, mixed> $o): void {}
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessKeyNotOffset,
            "array TKey on ArrayAccess must emit 4330: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessIssetUnset_UsesTKey()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function probe(\ArrayAccess<string, User> $o, string $k): void {
                if (isset($o[$k])) {
                }
                unset($o[$k]);
            }
            """);

        errors.Should().BeEmpty(
            "isset/unset on ArrayAccess should accept TKey: " + Describe(errors));
    }

    [Fact]
    public void Check_ImplementorIndex_IsTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            class Bag implements \ArrayAccess<string, User> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(string $offset): User {
                    return new User();
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            function read(Bag $o, string $k): User {
                return $o[$k];
            }
            """);

        errors.Should().BeEmpty(
            "class implementing ArrayAccess<string, User> should index as User: "
            + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessStructTKey_IndexingReports4330Once()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Config = struct {
                string $host = '';
            };
            function f(\ArrayAccess<Config, mixed> $o): void {
                $o["x"];
            }
            """);

        errors.Should().ContainSingle(
            e => e.Code == MessageCode.CheckerArrayAccessKeyNotOffset,
            "indexing an ArrayAccess with a struct TKey must not emit 4330 at both the "
            + "parameter annotation and the index site: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessStructTKey_SubstitutedImplementorStillReports4330()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Config = struct {
                string $host = '';
            };
            class Box<T> implements \ArrayAccess<T, mixed> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(mixed $offset): mixed {
                    return null;
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            function f(Box<Config> $o): mixed {
                return $o["x"];
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessKeyNotOffset,
            "substituted ArrayAccess TKey on an implementor must still emit 4330: "
            + Describe(errors));
    }

    [Fact]
    public void Check_ArrayAccessStructTKey_IndexingDoesNotCascadeTypeMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Config = struct {
                string $host = '';
            };
            function f(\ArrayAccess<Config, mixed> $o): mixed {
                return $o["x"];
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "an illegal (struct) TKey must not cascade into a spurious index-vs-TKey type "
            + "mismatch on top of 4330: " + Describe(errors));
        errors.Should().ContainSingle(
            e => e.Code == MessageCode.CheckerArrayAccessKeyNotOffset,
            "struct TKey on ArrayAccess must still emit 4330 once: " + Describe(errors));
    }

    [Fact]
    public void Check_BareArrayAfterIsArray_AcceptsStringKey()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function read(mixed $value, string $segment): mixed {
                if (\is_array($value)) {
                    return $value[$segment];
                }
                return null;
            }
            """);

        errors.Should().BeEmpty(
            "bare array / is_array is array<int|string, mixed>, so string keys are legal: "
            + Describe(errors));
    }

    [Fact]
    public void Check_OneArgArrayShorthand_AcceptsStringKey()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function read(array<string> $value, string $segment): string {
                return $value[$segment];
            }
            """);

        errors.Should().BeEmpty(
            "array<V> is array<int|string, V>, so string keys are legal: " + Describe(errors));
    }

    [Fact]
    public void Check_IntKeyedArray_RejectsStringKey()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function read(array<int, string> $value, string $segment): string {
                return $value[$segment];
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "explicit array<int, V> still rejects a string index: " + Describe(errors));
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
