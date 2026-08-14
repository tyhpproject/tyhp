using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// End-to-end regressions for gradual array assignability:
/// named struct → bare <c>array</c>, and <c>array&lt;K, V&gt;</c> ↔ bare <c>array</c>
/// in return and typed-local declaration positions (FOUND_BUGS Story 11 struct #1 / #6).
/// </summary>
[Trait("Category", "Checker")]
public class GradualArrayAssignabilityTests
{
    [Fact]
    public void NamedStruct_ReturnedAsBareArray_No4009()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x = 0;
                int $y = 0;
            }
            function make(): array {
                return new Point();
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"struct→array return must be assignable: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void NamedStruct_AssignedToBareArrayLocal_No4008()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x = 0;
            }
            function f(): void {
                array $a = new Point();
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch,
            $"struct→array local must be assignable: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void TypedArray_ReturnedAsBareArray_No4009()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function values(array<int, string> $a): array {
                return $a;
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"array<K,V>→array return must be assignable: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void TypedArray_AssignedToBareArrayLocal_No4008()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function f(array<string, int> $typed): void {
                array $bare = $typed;
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch,
            $"array<K,V>→array local must be assignable: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void BareArray_AssignedToTypedArrayLocal_No4008()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function f(array $bare): void {
                array<int, string> $typed = $bare;
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch,
            $"array→array<K,V> local must be gradually assignable: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void ChildStruct_ReturnedAsArrayOfParentValueType_StillRejected()
    {
        // The parent contributes an `int` property that the child does not redeclare. If
        // `CollectStructProperties` failed to walk the inheritance chain, the value union computed
        // for the child would only be `string` (its own property) and this would be incorrectly
        // accepted.
        var errors = CompileAndCheck("""
            <?tyhp
            struct ParentShape {
                int $count = 0;
            }
            struct ChildShape extends ParentShape {
                string $name = '';
            }
            function make(): array<string, string> {
                return new ChildShape();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"child struct's array value type must account for the inherited `int` property: {Describe(errors)}");
    }

    [Fact]
    public void ChildStruct_ReturnedAsArrayCoveringInheritedAndOwnValueTypes_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            struct ParentShape {
                int $count = 0;
            }
            struct ChildShape extends ParentShape {
                string $name = '';
            }
            function make(): array<string, int|string> {
                return new ChildShape();
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"child struct's array value type union must include both inherited and own property types: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Struct_ReturnedAsIntKeyedArray_StillRejected()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x = 0;
            }
            function make(): array<int, mixed> {
                return new Point();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
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
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
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
