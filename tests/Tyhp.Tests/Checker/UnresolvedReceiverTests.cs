using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Unresolved recovery must not silently satisfy member access, calls, or indexing
/// (Story 21.12 Workstream C / FOUND #56). Mixed receivers stay on TYHP4160.
/// </summary>
[Trait("Category", "Checker")]
public class UnresolvedReceiverTests
{
    [Fact]
    public void Check_UnknownFunctionMemberAccess_ReportsOnlyUndefinedFunction()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                unknownFn()->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_UnknownFunctionMethodCall_ReportsOnlyUndefinedFunction()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                unknownFn()->foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_SyntheticUnresolvedPropertyRead_Reports4197()
    {
        // Undeclared local: InferVariable yields Unresolved with no prior diagnostic on that
        // expression (the foreach-shaped hole from FOUND #44 is the same recovery marker).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedMethodCall_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole->foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedIndex_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                mixed $item = $hole[0];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedPropertyWrite_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole->x = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_ChainedIndexOnUnresolved_ReportsSingleDiagnostic()
    {
        // `$hole[0]` alone already reports TYHP4197 on the receiver. The *value* read out of that
        // index must stay `Unresolved` (not fall through to `mixed`), or the outer `->bar` /
        // `[1]` use would pile on an unrelated TYHP4160 for the same unresolved failure.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole[0]->bar;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_ChainedDoubleIndexOnUnresolved_ReportsSingleDiagnostic()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole[0][1];
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedInvoke_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedNullsafePropertyRead_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole?->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedNullsafeMethodCall_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole?->foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedStaticMethod_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole::foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedListDestructure_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                list($a) = $hole;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_CoalesceLeftUnresolvedMember_DoesNotReport4197()
    {
        // `??` left operands are existence probes — same skip as mixed use-site (TYHP4160).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): mixed {
                return $hole->x ?? 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_SyntheticUnresolvedDestructure_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                [$a] = $hole;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_UnknownFunctionDestructure_ReportsOnlyUndefinedFunction()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                [$a] = unknownFn();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_SyntheticUnresolvedClassConstant_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole::FOO;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_ChainedPropertyOnUnresolved_ReportsSingleDiagnostic()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole->x->y;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_TypedObjectPropertyRead_DoesNotReport4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Point {
                public int $x = 0;
            }

            function demo(Point $p): int {
                return $p->x;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_UnknownFunctionIndex_ReportsOnlyUndefinedFunction()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                unknownFn()[0];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_MixedPropertyAccess_Uses4160Not4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $m): void {
                $m->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_MixedMethodCall_Uses4160Not4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $m): void {
                $m->foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_MixedIndex_Uses4160Not4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $m): void {
                mixed $item = $m[0];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_CoalesceAssignLeftMixedMember_DoesNotReport4160()
    {
        // Same existence-probe suppression as the Unresolved `??=` case above, for `mixed`'s own
        // TYHP4160 use-site check.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $m): void {
                $m->x ??= 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_ThisChainedInScalarExtensionMethod_DoesNotReport4197()
    {
        // Regression: InlineSpliceRule's early accessibility pre-pass (SpliceAnalysis) resolves
        // `$this` inside a *sibling* extension method's body to check member accessibility. For a
        // scalar receiver (`extends int $this`), that pre-pass used to fall back to
        // `CheckerRuleContext.ResolveExpressionType` on a synthetic state with no `$this` binding,
        // which cached `Unresolved` on the `$this` AST node — then the real checker pass read that
        // stale cached type and falsely reported TYHP4197 on a perfectly valid
        // `$this->doubled()` call (Story 21.12 Workstream C review).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension MathOps extends int {
                function doubled(): int {
                    return $this + $this;
                }

                function quadrupled(): int {
                    return $this->doubled()->doubled();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_ThisChainedWithArgumentInScalarExtensionMethod_DoesNotReport4197()
    {
        // Same regression as above, but the outer call in the chain has an argument. That takes
        // `InlineSpliceRule`'s `&`-ref mutation pre-pass (`WrittenParameterNames`, via
        // `ResolveCallReceiverType`) down a code path independent of the accessibility pre-pass
        // above — both used to fall back to `ResolveExpressionType` on a state with no `$this`
        // binding and needed the same fix.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension MathOps extends int {
                function doubled(): int {
                    return $this + $this;
                }

                function addOne(int $n): int {
                    return $this + $n;
                }

                function quadrupled(): int {
                    return $this->doubled()->addOne(1);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_CoalesceAssignLeftUnresolvedMember_DoesNotReport4197()
    {
        // `??=`'s left is the same existence probe as bare `??` (NullSafetyRule treats both the
        // same way). `TypeCompatibilityRule.CheckBinaryOp` used to re-validate an assignment
        // operator's left receiver unconditionally — a leftover from the plain `=` write-target
        // case, where NullSafetyRule never visits the left at all — and that ran *before*
        // NullSafetyRule set the probe flag for this same node, so `$hole->x ??= 1` reported
        // TYHP4197 even though `$hole->x ?? 1` did not (Story 21.12 Workstream C review).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole->x ??= 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_ArrayWriteOnUnresolved_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole[0] = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    [Fact]
    public void Check_SyntheticUnresolvedStaticProperty_Reports4197()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $hole::$prop;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_UnresolvedAssignedToTypedTarget_DoesNotBlockAssignability()
    {
        // Global Unresolved ↔ T assignability is unchanged; only the member-access use site errors.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $s = $hole;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
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
