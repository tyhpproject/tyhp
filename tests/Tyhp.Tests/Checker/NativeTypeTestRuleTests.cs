using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.12 B — <c>#[\Tyhp\NativeTypeTest]</c> target, guard, single-type, and duplicate rules.
/// </summary>
[Trait("Category", "Checker")]
public class NativeTypeTestRuleTests
{
    [Fact]
    public void Check_NativeTypeTestOnInstanceMethod_Reports4340()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                #[\Tyhp\NativeTypeTest]
                public function isStr(mixed $v): $v is string {
                    return \is_string($v);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget);
    }

    [Fact]
    public void Check_NativeTypeTestOnStaticMethod_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                #[\Tyhp\NativeTypeTest]
                public static function isStr(mixed $v): $v is string {
                    return \is_string($v);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget
            || d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard
            || d.Code == MessageCode.CheckerNativeTypeTestTypeNotSingle
            || d.Code == MessageCode.CheckerNativeTypeTestExtraParamsNeedDefaults
            || d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    [Fact]
    public void Check_NativeTypeTestOnAbstractMethod_Reports4340()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class Holder {
                #[\Tyhp\NativeTypeTest]
                abstract public function isStr(mixed $v): $v is string;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget);
    }

    [Fact]
    public void Check_NativeTypeTestOnInterfaceMethod_Reports4340()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Holder {
                #[\Tyhp\NativeTypeTest]
                public function isStr(mixed $v): $v is string;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget);
    }

    [Fact]
    public void Check_NativeTypeTestWithoutTypeGuard_Reports4341()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isStr(mixed $v): bool {
                return \is_string($v);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard);
    }

    [Fact]
    public void Check_NativeTypeTestGuardNotFirstParam_Reports4341()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isStr(int $n, mixed $v): $v is string {
                return \is_string($v);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard);
    }

    [Fact]
    public void Check_NativeTypeTestOnUnionGuard_Reports4342()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isNumericUnion(mixed $v): $v is int|float|string {
                return \is_numeric($v);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestTypeNotSingle);
    }

    [Fact]
    public void Check_NativeTypeTestExtraRequiredParam_Reports4343()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isStrWithFlag(mixed $v, bool $flag): $v is string {
                return \is_string($v);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerNativeTypeTestExtraParamsNeedDefaults);
    }

    [Fact]
    public void Check_DuplicateNativeTypeTestForString_Reports4344()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isA(mixed $v): $v is string {
                return \is_string($v);
            }
            #[\Tyhp\NativeTypeTest]
            function isB(mixed $v): $v is string {
                return \is_string($v);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    [Fact]
    public void Check_OverlappingNativeTypeTestUnionAndString_Reports4344()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isStr(mixed $v): $v is string {
                return \is_string($v);
            }
            #[\Tyhp\NativeTypeTest]
            function isNumericish(mixed $v): $v is int|float|string {
                return \is_numeric($v) || \is_string($v);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    [Fact]
    public void Check_NonOverlappingIntAndString_AcceptedIncludingStaticMethod()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isIntVal(mixed $v): $v is int {
                return \is_int($v);
            }
            class Holder {
                #[\Tyhp\NativeTypeTest]
                public static function isStr(mixed $v): $v is string {
                    return \is_string($v);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget
            || d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard
            || d.Code == MessageCode.CheckerNativeTypeTestTypeNotSingle
            || d.Code == MessageCode.CheckerNativeTypeTestExtraParamsNeedDefaults
            || d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    [Fact]
    public void Check_ClassSpecificGuardVsObjectGuard_DoesNotOverlap()
    {
        // Every class is a subtype of the builtin `object`, but `$x is object` and
        // `$x is PositiveInt` are different registration keys and never compete at an actual
        // lookup site, so marking `object` (as the `is_object` overlay does) must not block a
        // class-specific static-method NativeTypeTest from also registering.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isObj(mixed $v): $v is object {
                return \is_object($v);
            }
            class PositiveInt {}
            class Guards {
                #[\Tyhp\NativeTypeTest]
                public static function isPositiveInt(mixed $v): $v is PositiveInt {
                    return $v instanceof PositiveInt;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    [Fact]
    public void Check_NativeTypeTestStaticMethodWithoutTypeGuard_Reports4341()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                #[\Tyhp\NativeTypeTest]
                public static function isStr(mixed $v): bool {
                    return \is_string($v);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard);
    }

    [Fact]
    public void Check_ValidNativeTypeTest_NoNativeTypeTestErrors()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\NativeTypeTest]
            function isPositiveInt(mixed $v): $v is int {
                return \is_int($v) && $v > 0;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget
            || d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard
            || d.Code == MessageCode.CheckerNativeTypeTestTypeNotSingle
            || d.Code == MessageCode.CheckerNativeTypeTestExtraParamsNeedDefaults
            || d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    [Fact]
    public void Check_UnmarkedGuard_DoesNotReportNativeTypeTest()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isMoney(mixed $v): $v is string {
                return \is_string($v);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerNativeTypeTestInvalidTarget
            || d.Code == MessageCode.CheckerNativeTypeTestRequiresTypeGuard
            || d.Code == MessageCode.CheckerNativeTypeTestTypeNotSingle
            || d.Code == MessageCode.CheckerNativeTypeTestExtraParamsNeedDefaults
            || d.Code == MessageCode.CheckerNativeTypeTestDuplicate);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "nativetypetest.tyhp");
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
