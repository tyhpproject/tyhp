using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers FOUND_BUGS prop-init item 40: PHP fatals on <c>return &lt;expr&gt;;</c> inside
/// <c>__construct</c>/<c>__destruct</c>; Tyhp must reject those at check time. Bare
/// <c>return;</c> remains legal, and ordinary methods without a return annotation still resolve
/// to <c>mixed</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ConstructorDestructorReturnRuleTests
{
    [Fact]
    public void Check_ValueReturnInConstructor_Reports4153()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private int $n;
                public function __construct(int $n): void {
                    $this->n = $n;
                    return $n;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue);
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue
            && d.Message.Contains("__construct", StringComparison.Ordinal));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_ValueReturnInDestructor_Reports4153()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function __destruct(): void {
                    return 1;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue);
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue
            && d.Message.Contains("__destruct", StringComparison.Ordinal));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BareReturnInConstructor_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private int $n;
                public function __construct(int $n): void {
                    if ($n < 0) {
                        return;
                    }
                    $this->n = $n;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue
            || d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_BareReturnInDestructor_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function __destruct(): void {
                    if (true) {
                        return;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue
            || d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_UntypedOrdinaryMethod_StillAcceptsAnyReturn()
    {
        // Non-magic methods with no return annotation still resolve ExpectedReturnType to mixed;
        // ctor/dtor void-forcing must not change that.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function identity(int $n) {
                    return $n;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue
            || d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_ClosureReturnValueInsideConstructor_DoesNotReport4153()
    {
        // A closure declared inside __construct is its own callable — returning a value from it is
        // completely legal PHP and must not be attributed to the enclosing constructor. The closure
        // declares its own `: int` return type so the assignment below type-checks on its own merits
        // (an unannotated closure resolves to `mixed`, same as an unannotated ordinary method — see
        // Check_UntypedOrdinaryMethod_StillAcceptsAnyReturn — which is an unrelated strictness concern,
        // not part of what this test is verifying).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private int $n;
                public function __construct(int $n): void {
                    $fn = function (): int {
                        return 5;
                    };
                    $this->n = $fn();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue
            || d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ArrowFunctionReturnValueInsideDestructor_DoesNotReport4153()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function __destruct(): void {
                    $arrow = fn (int $x): int => $x + 1;
                    $arrow(1);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConstructorDestructorCannotReturnValue);
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
                PhpVersion = "8.4",
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
