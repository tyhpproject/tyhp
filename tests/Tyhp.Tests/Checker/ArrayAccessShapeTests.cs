using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 6d: <c>ArrayAccessShape&lt;TStruct&gt;</c> per-key indexing,
/// TYHP4331 wide keys, TYHP4332 append, and TYHP4333 unhandled keys.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.6")]
public class ArrayAccessShapeTests
{
    private const string ConfigMap = """
        type ConfigMap = struct {
            string $host = '';
            int $port = 0;
        };
        """;

    [Fact]
    public void Check_LiteralHostIndex_IsString()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            function read(\Tyhp\Contracts\ArrayAccessShape<ConfigMap> $o): string {
                return $o['host'];
            }
            """);

        errors.Should().BeEmpty(
            "$o['host'] on ArrayAccessShape<ConfigMap> should be string: " + Describe(errors));
    }

    [Fact]
    public void Check_LiteralPortIndex_IsInt()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            function read(\Tyhp\Contracts\ArrayAccessShape<ConfigMap> $o): int {
                return $o['port'];
            }
            """);

        errors.Should().BeEmpty(
            "$o['port'] on ArrayAccessShape<ConfigMap> should be int: " + Describe(errors));
    }

    [Fact]
    public void Check_WideStringKey_Reports4331()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            function read(\Tyhp\Contracts\ArrayAccessShape<ConfigMap> $o, string $s): mixed {
                return $o[$s];
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeWideKey,
            "wide string key must emit 4331: " + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "wide key should not also be a generic type mismatch: " + Describe(errors));
    }

    [Fact]
    public void Check_Append_Reports4332()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            function write(\Tyhp\Contracts\ArrayAccessShape<ConfigMap> $o): void {
                $o[] = 'x';
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeAppend,
            "$obj[] = on ArrayAccessShape must emit 4332: " + Describe(errors));
    }

    [Fact]
    public void Check_OffsetGetCoveringOnlyHost_Reports4333ForPort()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            class Config implements \Tyhp\Contracts\ArrayAccessShape<ConfigMap> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(mixed $offset): mixed {
                    if ($offset === 'host') {
                        return '';
                    }
                    throw new \RuntimeException();
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                    if ($offset === 'host') {
                        $unused = $value;
                        return;
                    }
                    if ($offset === 'port') {
                        $unused = $value;
                        return;
                    }
                    throw new \RuntimeException();
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeUnhandledKey
                && e.Message.Contains("'port'", StringComparison.Ordinal),
            "offsetGet covering only host must report unhandled 'port': " + Describe(errors));
    }

    [Fact]
    public void Check_ThrowOnPortPath_DoesNotCoverPort()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            class Config implements \Tyhp\Contracts\ArrayAccessShape<ConfigMap> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(mixed $offset): mixed {
                    if ($offset === 'host') {
                        return '';
                    }
                    if ($offset === 'port') {
                        throw new \RuntimeException();
                    }
                    throw new \RuntimeException();
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                    if ($offset === 'host') {
                        $unused = $value;
                        return;
                    }
                    if ($offset === 'port') {
                        $unused = $value;
                        return;
                    }
                    throw new \RuntimeException();
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeUnhandledKey
                && e.Message.Contains("'port'", StringComparison.Ordinal),
            "throw on the 'port' path must not cover that key: " + Describe(errors));
    }

    [Fact]
    public void Check_OffsetGetCoveringBothKeys_Succeeds()
    {
        var errors = CompileAndCheck($$"""
            <?tyhp
            {{ConfigMap}}
            class Config implements \Tyhp\Contracts\ArrayAccessShape<ConfigMap> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(mixed $offset): mixed {
                    if ($offset === 'host') {
                        return '';
                    }
                    if ($offset === 'port') {
                        return 0;
                    }
                    throw new \RuntimeException();
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                    if ($offset === 'host') {
                        $unused = $value;
                        return;
                    }
                    if ($offset === 'port') {
                        $unused = $value;
                        return;
                    }
                    throw new \RuntimeException();
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeUnhandledKey,
            "covering host and port should not emit 4333: " + Describe(errors));
    }

    [Fact]
    public void Check_HomogeneousArrayAccess_IsNotShape()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<string, User> $o, string $k): User {
                return $o[$k];
            }
            """);

        errors.Should().BeEmpty(
            "homogeneous ArrayAccess<string, User> must not take the shape path: "
            + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerArrayAccessShapeWideKey,
            "string TKey on homogeneous ArrayAccess is not a wide shape key: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

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
