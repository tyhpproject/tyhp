using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class MixedUseSiteRuleTests
{
    [Fact]
    public void Check_MixedMethodCall_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): void {}
            }

            function demo(mixed $value): void {
                $value->bar();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedPropertyAccess_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public string $name = '';
            }

            function demo(mixed $value): void {
                string $n = $value->name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedIndexAccess_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                mixed $item = $value[0];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedArithmetic_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                int $result = $value + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedConcat_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                string $s = $value . 'x';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedClone_Reports4073()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                mixed $copy = clone $value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCloneNonObject);
    }

    [Fact]
    public void Check_MixedInvoke_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $fn): void {
                $fn();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedForeach_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                foreach ($value as $item) {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedComparison_Allowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                if ($value === null) {
                }
                if ($value !== 0) {
                }
                if ($value == 'x') {
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedInstanceof_AllowedAndNarrows()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): void {}
            }

            function demo(mixed $value): void {
                if ($value instanceof Foo) {
                    $value->bar();
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MixedAfterIsString_ArithmeticAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): int {
                if (\is_int($value)) {
                    return $value + 1;
                }
                return 0;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MixedAfterIsArray_IndexAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                if (\is_array($value)) {
                    mixed $item = $value[0];
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MixedPassedToMixedParam_Allowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function logValue(mixed $v): void {}

            function demo(mixed $value): void {
                logValue($value);
                if (\is_string($value)) {
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_MixedAssignmentToMixed_Allowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): mixed {
                mixed $copy = $value;
                return $copy;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MixedCompoundAssign_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                $value += 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SwitchTrue_TypeGuardNarrowsMixedForMemberAccess()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): void {}
            }

            function demo(mixed $value): void {
                switch (true) {
                    case $value instanceof Foo:
                        $value->bar();
                        break;
                    default:
                        break;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
