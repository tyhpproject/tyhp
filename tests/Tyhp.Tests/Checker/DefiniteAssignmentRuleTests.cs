using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class DefiniteAssignmentRuleTests
{
    [Fact]
    public void Check_UnassignedTypedLocalRead_Reports4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                int $x;
                return $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_AssignedTypedLocalRead_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                int $x = 1;
                return $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_AssignedOnlyInOneIfBranch_Reports4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool $cond): int {
                int $x;
                if ($cond) {
                    $x = 1;
                }
                return $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_AssignedInBothIfBranches_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool $cond): int {
                int $x;
                if ($cond) {
                    $x = 1;
                } else {
                    $x = 2;
                }
                return $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_AssignedInBothTernaryArms_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool $cond): int {
                int $x;
                $cond ? ($x = 1) : ($x = 2);
                return $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_AssignedInOneTernaryArm_Reports4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool $cond): int {
                int $x;
                $cond ? ($x = 1) : 0;
                return $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_SimpleAssignWriteTarget_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                int $x;
                $x = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_IssetGuard_ClearsPossiblyUndefined()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                int $x;
                if (isset($x)) {
                    return $x;
                }
                return 0;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_ReassignedInLoop_AfterPriorInit_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array<string> $items): string {
                string $body = '';
                for (int $i = 0; $i < \count($items); $i++) {
                    if ($items[$i] !== '') {
                        $body = $body . $items[$i];
                    } else {
                        $body = $body . '_';
                    }
                }
                return $body;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_TernaryAsAssignmentRhs_ArmReadsUnassignedVariable_Reports4014()
    {
        // Regression test: a ternary used as a plain assignment's RHS is *also* reached through
        // TypeCompatibilityRule.CheckBinaryOp's assignability check (which resolves the RHS type
        // before NullSafetyRule re-walks it via CheckNode). Previously, whichever path ran first
        // already merged both arms' assignment effects into the live parent state, so the second
        // path derived its "before the ternary" branch snapshot from an already-merged baseline —
        // masking this exact read (see TypeInferrer.Expressions.cs InferTernary / ControlFlowRule
        // CheckTernary for the fix).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool $cond): int {
                int $y;
                int $z;
                int $x;
                $x = $cond ? ($z = $y) : ($z = 2);
                return $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_TernaryAsAssignmentRhs_ReadInOneArm_Reports4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool $cond): int {
                int $z;
                int $result;
                $result = $cond ? $z : ($z = 10);
                return $result;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_IfWithoutElse_EarlyReturnOnNullGuard_NarrowsFallThroughToNonNull()
    {
        // Regression test: `if ($narrow === null) { throw/return; }` with no else must apply the
        // negative narrowing to the fall-through path. CheckIf previously merged the (dead, always
        // returning) then-branch's narrowed-to-null state straight into the un-narrowed parent
        // state instead, so `$narrow` stayed `?Base` after the guard (see CheckIf in
        // ControlFlowRule.cs).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
            }
            function demo(?Base $narrow): Base {
                if ($narrow === null) {
                    throw new \Exception('x');
                }
                return $narrow;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TernaryInstanceofNarrowing_AfterEarlyReturnNullGuard_NoTypeMismatch()
    {
        // Regression test: an instanceof-narrowed ternary whose else-arm relies on a preceding
        // `if ($narrow === null) { return …; }` guard must see `$narrow` as non-null in that arm.
        // This previously produced a spurious TYHP4008 because the guard's negative narrowing
        // never reached the ternary's else arm (fixed alongside CheckIf in ControlFlowRule.cs).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
            }
            final class Named extends Base {
                public fn getUnderlying(): Base => $this;
            }
            function demo(Base $broad, ?Base $narrow): bool {
                if ($narrow === null) {
                    return false;
                }
                Base $narrowType = $narrow instanceof Named ? $narrow->getUnderlying() : $narrow;
                return $narrowType === $narrow;
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
