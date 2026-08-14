using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regressions for Elvis <c>?:</c> empty-check typing and parenthesized ternary conditions
/// (FOUND_BUGS Elvis audit #1 / #2 — false TYHP4043 on decimal IntegerScaledBackend).
/// </summary>
[Trait("Category", "Checker")]
public class ElvisAndTernaryConditionTests
{
    [Fact]
    public void Check_ElvisWithStringLeft_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $intPart, string $fracPart): string {
                string $combined = \ltrim($intPart . $fracPart, '0') ?: '0';
                return $combined;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"Elvis left is empty-checked (any type), not bool: {Describe(errors)}");
    }

    [Fact]
    public void Check_ElvisWithNonBoolLeft_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $digits): string {
                return \ltrim($digits, '0') ?: '0';
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"Elvis left may be string: {Describe(errors)}");
    }

    [Fact]
    public void Check_RealTernaryNonBoolCondition_StillReports4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $s): string {
                return $s ? 'yes' : 'no';
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"real ternary still requires a bool condition: {Describe(errors)}");
    }

    [Fact]
    public void Check_ParenthesizedBoolTernaryCondition_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(bool $negative, string $combined): string {
                return ($negative && $combined !== '0') ? '-' . $combined : $combined;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"parenthesized && / !== condition must type as bool: {Describe(errors)}");
    }

    [Fact]
    public void Check_ParenthesizedComparisonTernaryCondition_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(int $n): string {
                return ($n !== 0) ? 'nonzero' : 'zero';
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"parenthesized !== must type as bool: {Describe(errors)}");
    }

    [Fact]
    public void Check_DecimalStyleElvisThenParenthesizedTernary_No4043()
    {
        // Combined shape from IntegerScaledBackend.toScaled / fromScaled.
        var errors = CompileAndCheck("""
            <?tyhp
            function toDisplay(bool $negative, string $intPart, string $fracPart): string {
                string $combined = \ltrim($intPart . $fracPart, '0') ?: '0';
                return ($negative && $combined !== '0') ? '-' . $combined : $combined;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"decimal-style Elvis + parenthesized ternary: {Describe(errors)}");
    }

    [Fact]
    public void Check_NestedElvis_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $a, string $b, string $c): string {
                return $a ?: $b ?: $c;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"chained Elvis (`a ?: b ?: c`) is empty-checked at every level, not bool: {Describe(errors)}");
    }

    [Fact]
    public void Check_ParenthesizedElvisLeft_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $a, string $b): string {
                return ($a) ?: $b;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"parenthesized Elvis left operand still empty-checks, not bool: {Describe(errors)}");
    }

    [Fact]
    public void Check_ElvisInsideParenthesizedBinaryCondition_DoesNotReport4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $a, string $b, bool $c): string {
                return (($a ?: $b) !== '' && $c) ? 'x' : 'y';
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"Elvis nested inside a parenthesized bool-producing binary condition: {Describe(errors)}");
    }

    /// <summary>
    /// Elvis's bool exemption applies only to its own left operand — the exemption must not leak
    /// to an outer position that consumes the Elvis expression's *result*. A non-bool Elvis result
    /// used directly as a real ternary's condition (or an `if`/`while` condition) still requires an
    /// explicit `bool`, exactly like any other non-bool value in that position.
    /// </summary>
    [Fact]
    public void Check_NonBoolElvisResultAsRealTernaryCondition_StillReports4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $a, string $b): string {
                return ($a ?: $b) ? 'x' : 'y';
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"Elvis's own result (string) used as a real ternary condition still requires bool: {Describe(errors)}");
    }

    /// <summary>
    /// Same exemption-scoping check as above, for an `if` condition instead of a real ternary.
    /// </summary>
    [Fact]
    public void Check_NonBoolElvisResultAsIfCondition_StillReports4043()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $a, string $b): string {
                if ($a ?: $b) {
                    return $a;
                }
                return $b;
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"Elvis's own result (string) used as an if condition still requires bool: {Describe(errors)}");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    /// <summary>
    /// Compiles and checks a self-contained snippet and returns only the diagnostics that
    /// originate from the snippet file.
    /// </summary>
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
