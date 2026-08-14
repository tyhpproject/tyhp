using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers RESOLVED_BUGS item 36's policy reversal: nested named functions/methods bind cleanly
/// (so type resolution inside their signature/body still works), but the checker now rejects a
/// named function declared inside another named function or method's body with 4802 instead of
/// letting it compile. Closures, and named functions guarded at file scope (the
/// <c>if (!function_exists(...))</c> pattern), remain unaffected.
/// </summary>
[Trait("Category", "Checker")]
public class NestedFunctionRuleTests
{
    [Fact]
    public void Check_NamedFunctionNestedInsideMethod_Reports4802()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go(): void {
                    function nested(): void {}
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed
            && d.Message.Contains("nested", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_NamedFunctionNestedInsideFreeFunction_Reports4802()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function outer(): void {
                function nested(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    [Fact]
    public void Check_NamedFunctionNestedInsideConditionalBlockInMethod_StillReports4802()
    {
        // An intervening if/loop/try block does not create a new function boundary, so the
        // nested declaration is still rejected even though it is not a direct child statement
        // of the method body.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go(bool $flag): void {
                    if ($flag) {
                        function nested(): void {}
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    [Fact]
    public void Check_DoublyNestedNamedFunction_ReportsOncePerNestedDeclaration()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function outer(): void {
                function middle(): void {
                    function inner(): void {}
                }
            }
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed)
            .Should().Be(2);
    }

    [Fact]
    public void Check_TopLevelFunction_DoesNotReport4802()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function outer(): void {}
            class C {
                public function go(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    [Fact]
    public void Check_ClosureNestedInsideMethod_DoesNotReport4802()
    {
        // Closures/arrow functions are unnamed and are not subject to this restriction.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go(): void {
                    $fn = function (): void {};
                    $fn();
                    $arrow = fn (int $x): int => $x + 1;
                    $arrow(1);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    [Fact]
    public void Check_MethodOfClassDeclaredInsideFunction_DoesNotReport4802()
    {
        // A named class declared inside a function is a separate, pre-existing PHP pattern
        // (conditional class declaration) — its methods belong to the class scope, not directly
        // to the enclosing function, so they are not "nested named functions".
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function outer(): void {
                class Inner {
                    public function m(): void {}
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    [Fact]
    public void Check_FunctionGuardedByFunctionExistsAtFileScope_DoesNotReport4802()
    {
        // The `if (!function_exists(...))` conditional-declaration pattern lives at file scope,
        // not inside another named function/method, so it must remain accepted.
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists(__NAMESPACE__ . '\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    [Fact]
    public void Check_NamedFunctionNestedInsideClosureInsideMethod_Reports4802()
    {
        // A closure does not clear `EnclosingCallable` (only `IsInsideClosure`, consulted by rules
        // that attribute a finding to that *specific* callable, e.g. the ctor/dtor return-value
        // check) — a named function declared inside it is still nested inside `go`'s body at
        // runtime and must still be rejected.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go(): void {
                    $fn = function (): void {
                        function nested(): void {}
                    };
                    $fn();
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNestedNamedFunctionNotAllowed);
    }

    private static DiagnosticBag CompileAndCheck(string content) =>
        CompileAndCheckCore(content, requireNoBindErrors: true);

    private static DiagnosticBag CompileAndCheckAllowBindWarnings(string content) =>
        CompileAndCheckCore(content, requireNoBindErrors: false);

    private static DiagnosticBag CompileAndCheckCore(string content, bool requireNoBindErrors)
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
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            if (requireNoBindErrors)
            {
                result.Diagnostics.HasErrors.Should().BeFalse("binding should not report errors");
            }

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
