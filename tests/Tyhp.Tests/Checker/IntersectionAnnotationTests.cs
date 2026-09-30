using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Inhabitable <c>A&amp;B</c> annotations must not collapse to <c>never</c> (TYHP4061)
/// when neither arm is a subtype of the other.
/// </summary>
[Trait("Category", "Checker")]
public class IntersectionAnnotationTests
{
    [Fact]
    public void Check_UnrelatedInterfaces_ParameterAnnotation_DoesNotReport4061()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            interface A {
                public function a(): void;
            }
            interface B {
                public function b(): void;
            }
            function demo(A&B $value): void {
                $value->a();
                $value->b();
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNeverNotAllowedHere,
            "A&B of unrelated interfaces must stay inhabitable: " + Describe(errors));
        errors.Should().BeEmpty("inhabitable A&B parameter should type-check: " + Describe(errors));
    }

    [Fact]
    public void Check_UnrelatedInterfaces_ReturnAndProperty_DoNotReport4061()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            interface A {}
            interface B {}
            class Holder {
                public function __construct(public A&B $both) {}
                public function get(): A&B {
                    return $this->both;
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNeverNotAllowedHere,
            "A&B return/property must stay inhabitable: " + Describe(errors));
        errors.Should().BeEmpty("inhabitable A&B return/property should type-check: " + Describe(errors));
    }

    [Fact]
    public void Check_ClassImplementingBoth_AssignableToIntersection()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            interface A {
                public function a(): void;
            }
            interface B {
                public function b(): void;
            }
            class Both implements A, B {
                public function a(): void {}
                public function b(): void {}
            }
            function demo(A&B $value): void {
                $value->a();
                $value->b();
            }
            function main(): void {
                demo(new Both());
            }
            """);

        errors.Should().BeEmpty(
            "a class implementing both arms must assign to A&B: " + Describe(errors));
    }

    [Fact]
    public void Check_OnlyOneArm_NotAssignableToIntersection()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            interface A {}
            interface B {}
            class OnlyA implements A {}
            function demo(A&B $value): void {}
            function pass(OnlyA $a): void {
                demo($a);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType,
            "A alone must not assign to A&B: " + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNeverNotAllowedHere,
            "the A&B parameter itself must not be never: " + Describe(errors));
    }

    [Fact]
    public void Check_AliasedUnrelatedInterfaces_DoesNotReport4061()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            interface A {}
            interface B {}
            type Both = A&B;
            function demo(Both $value): void {}
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNeverNotAllowedHere,
            "alias of A&B must stay inhabitable: " + Describe(errors));
        errors.Should().BeEmpty("aliased A&B parameter should type-check: " + Describe(errors));
    }

    [Fact]
    public void Check_IntAndString_StillRejected()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(int&string $value): void {}
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerNonClassInIntersection
                || e.Code == MessageCode.CheckerNeverNotAllowedHere,
            "int&string must remain illegal: " + Describe(errors));
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
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
