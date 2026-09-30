using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Call-site arity/shape for operator overloads: a unary <c>+</c> must not satisfy binary
/// <c>$a + $b</c> (and vice versa); unmatched object operands report TYHP4029 instead of
/// falling through to native PHP operators.
/// </summary>
[Trait("Category", "Checker")]
public class OperatorOverloadCallSiteArityTests
{
    [Fact]
    public void Check_UnaryPlusOnly_BinaryUsage_ReportsInvalidOperator()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $value): Box {
                    Box $result = new Box();
                    $result->value = $value->value;
                    return $result;
                }
            }

            function f(Box $a, Box $b): Box {
                return $a + $b;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType
            && HasParam(d, "+")
            && HasParam(d, "Box"));
    }

    [Fact]
    public void Check_BinaryPlusOnly_UnaryUsage_ReportsInvalidOperator()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $left, self $right): Box {
                    Box $result = new Box();
                    $result->value = $left->value + $right->value;
                    return $result;
                }
            }

            function f(Box $a): Box {
                return +$a;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType
            && HasParam(d, "+")
            && HasParam(d, "Box"));
    }

    [Fact]
    public void Check_BinaryPlusWrongOperandShape_ReportsInvalidOperator()
    {
        // Only `+(self, int)` — Box+Box must not silently accept via first-arity fallback.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $left, int $right): Box {
                    Box $result = new Box();
                    $result->value = $left->value + $right;
                    return $result;
                }
            }

            function f(Box $a, Box $b): Box {
                return $a + $b;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_NoPlusOverload_BinaryUsage_ReportsInvalidOperator()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;
            }

            function f(Box $a, Box $b): Box {
                return $a + $b;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_MatchingBinaryPlus_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $left, self $right): Box {
                    Box $result = new Box();
                    $result->value = $left->value + $right->value;
                    return $result;
                }
            }

            function f(Box $a, Box $b): Box {
                return $a + $b;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_MatchingUnaryPlus_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $value): Box {
                    Box $result = new Box();
                    $result->value = $value->value;
                    return $result;
                }
            }

            function f(Box $a): Box {
                return +$a;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_UnaryPlusOnly_CompoundAssign_ReportsInvalidOperator()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $value): Box {
                    return $value;
                }
            }

            function f(Box $a, Box $b): void {
                $a += $b;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType
            && HasParam(d, "+"));
    }

    [Fact]
    public void Check_MixedScalarAndObject_WrongOperandShape_ReportsInvalidOperator()
    {
        // Only `+(self, self)` — Box + int must not silently fall through to native PHP.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $left, self $right): Box {
                    Box $result = new Box();
                    $result->value = $left->value + $right->value;
                    return $result;
                }
            }

            function f(Box $a): Box {
                return $a + 5;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_MixedScalarAndObject_MatchingShape_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $left, int $right): Box {
                    Box $result = new Box();
                    $result->value = $left->value + $right;
                    return $result;
                }
            }

            function f(Box $a): Box {
                return $a + 5;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_NativeScalarArithmetic_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(int $a, int $b): int {
                return $a + $b;
            }

            function g(string $a, string $b): string {
                return $a . $b;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_PostfixIncrementNoOverload_ReportsInvalidOperator()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;
            }

            function f(Box $a): void {
                $a++;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_PrefixIncrementMatchingOverload_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator ++(self $value): Box {
                    Box $result = new Box();
                    $result->value = $value->value + 1;
                    return $result;
                }
            }

            function f(Box $a): Box {
                return ++$a;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_PostfixIncrementMatchingOverload_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator ++(self $value): Box {
                    Box $result = new Box();
                    $result->value = $value->value + 1;
                    return $result;
                }
            }

            function f(Box $a): void {
                $a++;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_ObjectComparisonWithoutOverload_NoInvalidOperatorError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;
            }

            function f(Box $a, Box $b): bool {
                return $a == $b;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    [Fact]
    public void Check_UnresolvedOperand_DoesNotReportInvalidOperator()
    {
        // $undefinedVar's type resolves to the checker's Unresolved error-recovery marker —
        // that's a separate resolution failure, not evidence the overload shape is wrong.
        // TYHP4029 must not cascade on top of it.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;

                operator +(self $left, self $right): Box {
                    Box $result = new Box();
                    $result->value = $left->value + $right->value;
                    return $result;
                }
            }

            function f(Box $a): Box {
                return $a + $undefinedVar;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
    }

    private static bool HasParam(IDiagnostic diagnostic, string substring)
        => diagnostic.FormatParams is { } args
            && args.Any(p => (p?.ToString() ?? string.Empty)
                .Contains(substring, StringComparison.OrdinalIgnoreCase));

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
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
