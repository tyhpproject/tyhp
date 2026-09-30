using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// TYHP4012 (<see cref="MessageCode.CheckerUnreachableCode"/>) on statements that cannot
/// run after a definite terminator. One warning per statement list (no flood).
/// </summary>
[Trait("Category", "Checker")]
public class UnreachableCodeRuleTests
{
    [Fact]
    public void Check_StatementAfterReturn_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                return 1;
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_StatementAfterThrow_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                throw new \Exception("fail");
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterExit_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                exit(1);
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterDie_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                die;
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_MultipleStatementsAfterReturn_ReportsOnce()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                return 1;
                echo "a";
                echo "b";
                echo "c";
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_NestedBlockAfterReturn_ReportsOnceOnTheBlock()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                return 1;
                {
                    echo "a";
                    echo "b";
                }
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_StatementAfterBreakInLoop_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): void {
                while (true) {
                    break;
                    echo "nope";
                }
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterContinueInLoop_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): void {
                while (true) {
                    continue;
                    echo "nope";
                }
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterIfElseBothReturn_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(bool $c): int {
                if ($c) {
                    return 1;
                } else {
                    return 2;
                }
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterIfReturnWithoutElse_IsReachable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(bool $c): int {
                if ($c) {
                    return 1;
                }
                return 0;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_DeadCodeInsideThenBlock_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(bool $c): int {
                if ($c) {
                    return 1;
                    echo "nope";
                }
                return 0;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_EmptyStatementAfterReturn_SkipsToNextRealStatement()
    {
        // A bare `;` (PhpNopStatementAst) right after the terminator is not itself worth a
        // 4012 — the warning must land on the next real statement instead of the `;`, and only
        // once.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                return 1;
                ;
                echo "nope";
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_DeadStatement_IsStillTypeChecked()
    {
        // The dead `return "wrong";` gets TYHP4012 (unreachable) *and* is still type-checked
        // against the declared `: int` return type (TYHP4009) — dead code is not skipped.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                return 1;
                return "wrong";
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_ReachableStatements_DoNotReport4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                echo "ok";
                return 1;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnreachableCode);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_StatementAfterBareIfElseBothExit_Reports4012()
    {
        // Braceless if/else arms (no `{ }`) must still mark the branch as terminated when the
        // arm body itself is `exit`/`die` — this bypasses CheckStatementSequence, which is where
        // exit/die/never detection normally lives.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(bool $c): int {
                if ($c) exit;
                else exit;
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterBareIfElseBothNeverCall_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function boom(): never {
                throw new \Exception("fail");
            }
            function f(bool $c): int {
                if ($c) boom();
                else boom();
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_StatementAfterThrowInsideTryWithFinallyNoCatch_ReportsUnreachableInsideTryAndAfter()
    {
        // No catch clause: an unconditional throw in `try` propagates past `finally`, so both the
        // dead statement inside `try` (after the throw) and the statement after the whole
        // try/finally are unreachable. The `finally` body itself must stay reachable.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                try {
                    throw new \Exception("fail");
                    echo "dead-in-try";
                } finally {
                    echo "cleanup";
                }
                echo "dead-after";
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(2);
    }

    [Fact]
    public void Check_FinallyBodyReachable_EvenWhenTryAlwaysThrows()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                try {
                    throw new \Exception("fail");
                } finally {
                    echo "cleanup";
                    echo "still-reachable";
                }
                echo "dead-after";
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_DeadCodeInsideSwitchCaseBody_Reports4012()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(int $x): int {
                switch ($x) {
                    case 1:
                        return 1;
                        echo "nope";
                    default:
                        return 0;
                }
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnreachableCode).Should().Be(1);
    }

    [Fact]
    public void Check_MatchAlwaysMatchingArm_ReportsOnly4208NotAlso4012()
    {
        // An always-true earlier arm makes later match arms unreachable — that is TYHP4208
        // (CheckerUnreachableArm), a separate CodeQualityRule mechanism untouched by this fix.
        // Match arms never go through CheckStatementSequence, so this must not also emit 4012.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): mixed {
                return match (true) {
                    1 === 1 => 1,
                    2 === 2 => 2,
                    default => 3,
                };
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableArm);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnreachableCode);
    }

    [Fact]
    public void Check_NeverReturningCall_Reports4012OnFollowingStatement()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function boom(): never {
                throw new \Exception("fail");
            }
            function f(): int {
                boom();
                echo "nope";
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableCode);
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
