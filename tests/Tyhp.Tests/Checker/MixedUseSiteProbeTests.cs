using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Review probes for Top-type #1 (TYHP4160 <c>mixed</c> use-site enforcement) covering edge cases
/// not exercised by <see cref="MixedUseSiteRuleTests"/>: casts, <c>?mixed</c>, <c>??</c>/<c>??=</c>,
/// ternary-joined mixed, and — most importantly — regression coverage for a real gap found and
/// fixed during review: <c>return</c>/<c>echo</c>/<c>yield</c> expressions skipped
/// <see cref="TypeCompatibilityRule"/>'s binary/unary operator checks (including TYHP4160) because
/// <c>ControlFlowRule</c> suppresses child traversal on those statements and
/// <c>CheckerHelpers.CheckCompileTimeConstructsInTree</c> had no fallback case for a bare
/// <c>PhpBinaryOpAst</c>/<c>PhpUnaryOpAst</c> (only specific forms — assign-write, coalesce,
/// logical-and, <c>with</c>, <c>await</c> — were dispatched). See RESOLVED_BUGS.md.
/// </summary>
[Trait("Category", "Checker")]
public class MixedUseSiteProbeTests
{
    [Fact]
    public void Probe_CastOnMixed_Allowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): int {
                return (int)$value;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Probe_NullableMixedArithmetic_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(?mixed $value): void {
                int $result = $value + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_CoalesceOnMixed_Allowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): mixed {
                return $value ?? 'default';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Probe_CoalesceAssignOnMixed_Allowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                $value ??= 'default';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Probe_UnaryPlusOnMixed_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                mixed $x = +$value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_TernaryMixedBranch_StaysUnnarrowedInJoinedType()
    {
        // A ternary joining a `mixed` arm with a `string` arm absorbs to full `mixed`
        // (`TypeComparer.UnionTypesCore` short-circuits on any mixed member), so the joined
        // value still requires narrowing — it must not silently narrow to `string`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value, bool $flag): void {
                mixed $joined = $flag ? $value : 'x';
                int $bad = $joined + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_OrOperator_RightSideNotNegativelyNarrowed_StillRejectsMixed()
    {
        // `||`/`or` do not get progressive negative-narrowing (unlike `&&`'s positive narrowing in
        // `TypeCompatibilityRule.CheckBinaryOp`), so the right operand still sees `$value` as
        // unnarrowed `mixed` here even though the left operand's negation implies `is_string`.
        // This is conservative (over-restrictive), not unsound, so it is documented as expected
        // current behavior rather than filed as a bug.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): bool {
                return !\is_string($value) || $value->bar();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_MixedArithmeticInReturn_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): int {
                return $value + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_MixedNegationInReturn_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): bool {
                return !$value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_MixedArithmeticInEcho_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                echo $value + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_MixedNegationInEcho_Reports4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                echo !$value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_MixedArithmeticInIfCondition_Reports4160()
    {
        // Baseline: if/while/switch conditions were never affected by the return/echo/yield gap —
        // `ControlFlowRule.CheckConditionExpression` already runs a full `context.CheckNode` walk
        // in addition to `CheckBoolCondition`'s narrower compile-time-construct walk.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $value): void {
                if ($value + 1 > 0) {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_MixedMethodCallInReturn_Reports4160()
    {
        // Baseline: call/member-access forms were already exempt from the gap via the
        // `PhpDereferenceableAst` case in `CheckCompileTimeConstructsInTree`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): int { return 1; }
            }

            function demo(mixed $value): int {
                return $value->bar();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_PrivateMethodCallInReturn_StillReportsVisibility()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                private function secret(): int { return 1; }
            }

            function demo(Foo $f): int {
                return $f->secret();
            }
            """);

        diagnostics.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Probe_MixedUnionParam_Reports4054And4160()
    {
        // FOUND #1g: `mixed|string` must not bypass CheckerMixedInComposite or use-site TYHP4160.
        // Named `mixed` resolves via PhpNamedTypeAst; without singleton mapping / union-aware
        // IsUnnarrowedMixed both checks previously missed it.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed|string $value): int {
                return $value + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedInComposite);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_StringMixedUnionParam_OrderDoesNotMatter_Reports4054And4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string|mixed $value): int {
                return $value + 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedInComposite);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Probe_VoidMixedGenericConstraint_StillAllowed()
    {
        // Promise-style constraints intentionally use `void|mixed`; that position is exempt from 4054.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box<TReturn extends void|mixed = void> {
                public function get(): TReturn {
                    return default(TReturn);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedInComposite);
    }

    [Fact]
    public void Probe_StringIndexAccess_ResolvesToStringNotMixed()
    {
        // `$a[$i]` on a `string` receiver must type as `string`, not `mixed` (Top-type #1 fallout —
        // see FOUND_BUGS.md / RESOLVED_BUGS.md). Covers both the plain built-in and a `?:`-narrowed
        // union that isn't always subsumed down to plain `string`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $a): string {
                $a = \ltrim($a, '0') ?: '0';
                return $a . $a[0];
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
