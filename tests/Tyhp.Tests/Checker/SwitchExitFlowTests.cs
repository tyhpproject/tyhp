using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Control flow out of <c>switch</c>: <c>break</c> (and <c>continue</c> targeting the switch)
/// leaves the switch, while return / throw leave the function. A switch whose every path
/// returns or throws, including <c>default</c>, ends the function (no TYHP4011); without
/// <c>default</c> the no-match path still reaches the code after it.
/// </summary>
[Trait("Category", "Checker")]
public class SwitchExitFlowTests
{
    [Fact]
    public void AllArmsReturnWithDefault_NoMissingReturn()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function label(int $n): string {
                switch ($n) {
                    case 1:
                        return 'one';
                    case 2:
                        return 'two';
                    default:
                        return 'many';
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void AllArmsThrow_NoMissingReturn()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function fail(int $n): string {
                switch ($n) {
                    case 1:
                        throw new \LogicException('one');
                    default:
                        throw new \RuntimeException('other');
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void MixedReturnAndThrowArms_NoMissingReturn()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function label(int $n): string {
                switch ($n) {
                    case 1:
                        return 'one';
                    default:
                        throw new \InvalidArgumentException('bad');
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void FallthroughIntoReturningCase_NoMissingReturn()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n): int {
                $x = 0;
                switch ($n) {
                    case 1:
                        $x = 1;
                    case 2:
                        return $x;
                    case 3:
                    case 4:
                        return 4;
                    default:
                        return -1;
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void DefaultFallingIntoReturningCase_NoMissingReturn()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n): int {
                switch ($n) {
                    case 1:
                        return 1;
                    default:
                    case 2:
                        return 2;
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void MissingDefault_Reports4011()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function label(int $n): string {
                switch ($n) {
                    case 1:
                        return 'one';
                    case 2:
                        return 'two';
                }
            }
            """);

        Codes(result).Should().Contain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void BreakInOneArm_Reports4011()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function label(int $n): string {
                switch ($n) {
                    case 1:
                        return 'one';
                    case 2:
                        break;
                    default:
                        return 'many';
                }
            }
            """);

        Codes(result).Should().Contain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void BreakAfterReturnInArm_NoMissingReturn()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function label(int $n): string {
                switch ($n) {
                    case 1:
                        if ($n > 0) {
                            return 'one';
                        }
                        return 'zero';
                    default:
                        return 'many';
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void LastArmFallsOffEnd_Reports4011()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n): int {
                switch ($n) {
                    case 1:
                        return 1;
                    default:
                        $n++;
                }
            }
            """);

        Codes(result).Should().Contain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void CodeAfterAllReturningSwitch_Reports4012()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function label(int $n): string {
                switch ($n) {
                    case 1:
                        return 'one';
                    default:
                        return 'many';
                }
                return 'never';
            }
            """);

        Codes(result).Count(c => c == MessageCode.CheckerUnreachableCode).Should().Be(1, Describe(result));
    }

    [Fact]
    public void BreakTwoFromSwitchInLoop_LeavesLoop()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n, bool $go): int {
                while ($go) {
                    switch ($n) {
                        case 1:
                            break 2;
                        default:
                            return 0;
                    }
                }
                return 1;
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerBreakOutsideLoop, Describe(result));
        Codes(result).Should().NotContain(MessageCode.CheckerUnreachableCode, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void BreakTwoFromSwitchInLoop_CodeAfterSwitchInBodyIsUnreachable()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n, bool $go): int {
                while ($go) {
                    switch ($n) {
                        case 1:
                            break 2;
                        default:
                            return 0;
                    }
                    $n++;
                }
                return 1;
            }
            """);

        Codes(result).Count(c => c == MessageCode.CheckerUnreachableCode).Should().Be(1, Describe(result));
    }

    [Fact]
    public void BreakTwoFromNestedSwitch_LeavesOuterSwitch_Reports4011()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $a, int $b): string {
                switch ($a) {
                    case 1:
                        switch ($b) {
                            case 1:
                                break 2;
                            default:
                                return 'b';
                        }
                    default:
                        return 'a';
                }
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerBreakOutsideLoop, Describe(result));
        Codes(result).Should().Contain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void BreakThreeBeyondEnclosingConstructs_Reports4026()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n, bool $go): void {
                while ($go) {
                    switch ($n) {
                        default:
                            break 3;
                    }
                }
            }
            """);

        Codes(result).Should().Contain(MessageCode.CheckerBreakOutsideLoop, Describe(result));
    }

    [Fact]
    public void ContinueInsideSwitch_ActsAsBreak_CodeAfterSwitchReachable()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $limit): int {
                $total = 0;
                for ($i = 0; $i < $limit; $i++) {
                    switch ($i) {
                        case 0:
                            continue;
                        default:
                            return $i;
                    }
                    $total++;
                }
                return $total;
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerUnreachableCode, Describe(result));
        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void ContinueTwoInsideSwitch_ContinuesLoop_CodeAfterSwitchUnreachable()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $limit): int {
                $total = 0;
                for ($i = 0; $i < $limit; $i++) {
                    switch ($i) {
                        case 0:
                            continue 2;
                        default:
                            return $i;
                    }
                    $total++;
                }
                return $total;
            }
            """);

        Codes(result).Count(c => c == MessageCode.CheckerUnreachableCode).Should().Be(1, Describe(result));
        Codes(result).Should().NotContain(MessageCode.CheckerMissingReturnStatement, Describe(result));
    }

    [Fact]
    public void BreakInsideClosureInLoop_Reports4026()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(bool $go): void {
                while ($go) {
                    $g = function (): void {
                        break;
                    };
                    $g();
                }
            }
            """);

        Codes(result).Should().Contain(MessageCode.CheckerBreakOutsideLoop, Describe(result));
    }

    [Fact]
    public void VariableAssignedInEveryArmWithDefault_DefinitelyAssigned()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n): string {
                string $s;
                switch ($n) {
                    case 1:
                        $s = 'one';
                        break;
                    case 2:
                        return 'two';
                    default:
                        $s = 'other';
                }
                return $s;
            }
            """);

        Codes(result).Should().NotContain(MessageCode.CheckerVariablePossiblyUndefined, Describe(result));
        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void VariableAssignedInEveryArmWithoutDefault_PossiblyUndefined()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(int $n): string {
                string $s;
                switch ($n) {
                    case 1:
                        $s = 'one';
                        break;
                    case 2:
                        $s = 'two';
                        break;
                }
                return $s;
            }
            """);

        Codes(result).Should().Contain(MessageCode.CheckerVariablePossiblyUndefined, Describe(result));
    }

    [Fact]
    public void ReturningArmNarrowingHoldsAfterSwitch()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(?string $s): string {
                switch (true) {
                    case $s === null:
                        return '';
                    default:
                        break;
                }
                return $s;
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void BreakingArmNarrowingDoesNotHoldAfterSwitch()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function f(?string $s): string {
                switch (true) {
                    case $s === null:
                        break;
                    default:
                        return $s;
                }
                return $s;
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    private static IReadOnlyList<MessageCode> Codes(CompilationResult result) =>
        result.Diagnostics.Errors.Concat(result.Diagnostics.Warnings)
            .Where(d => (d.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Code)
            .ToList();

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Concat(result.Diagnostics.Warnings)
            .Select(d => $"{(int)d.Code}: {d.Message}"));
}
