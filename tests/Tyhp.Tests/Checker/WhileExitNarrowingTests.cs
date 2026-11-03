using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A <c>while</c> that exits because its condition became false narrows by that
/// negated condition; the state at each <c>break</c> joins that exit un-narrowed.
/// </summary>
[Trait("Category", "Checker")]
public class WhileExitNarrowingTests
{
    [Fact]
    public void WhileNotIsInt_AssignsInt_ExitIsInt()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m): int {
                while (!\is_int($m)) {
                    $m = 1;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void WhileWithBreak_DoesNotNarrowExit()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m): int {
                while (!\is_int($m)) {
                    break;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void WhileWithBreakAfterNarrowingAssignment_ExitIsNarrowed()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m, bool $stop): int {
                while (!\is_int($m)) {
                    if ($stop) {
                        $m = 2;
                        break;
                    }
                    $m = 1;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void BreakFromNestedIf_StateAtBreakJoinsExit()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m, bool $stop): int {
                while (!\is_int($m)) {
                    if ($stop) {
                        break;
                    }
                    $m = 1;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void BreakTwoFromInnerLoop_StateAtBreakJoinsOuterExit()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m, bool $stop): int {
                while (!\is_int($m)) {
                    while ($stop) {
                        break 2;
                    }
                    $m = 1;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
