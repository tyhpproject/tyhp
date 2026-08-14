using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 11 §8: binary/unary operator expressions infer the matching overload's declared return
/// type (same form selection as AliasConverter), not native PHP promotion.
/// </summary>
[Trait("Category", "Checker")]
public class OperatorOverloadReturnInferenceTests
{
    [Fact]
    public void Check_ShiftRight_OverloadReturn_AcceptedAsSelf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bits {
                public int $v = 0;
                operator >>(self $left, int $right): self {
                    $left->v = $left->v >> $right;
                    return $left;
                }
            }
            function shr(Bits $a): Bits {
                return $a >> 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_OverloadReturn_AcceptedAsSelf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function add(Money $a): Money {
                return $a + 10;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_OverloadReturn_RejectedWhenWrong()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function add(Money $a): int {
                return $a + 10;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_UnaryBitwiseNot_OverloadReturn_AcceptedAsSelf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bits {
                public int $v = 0;
                operator ~(self $value): self {
                    $value->v = ~$value->v;
                    return $value;
                }
            }
            function invert(Bits $a): Bits {
                return ~$a;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_Increment_OverloadReturn_AcceptedAsSelf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
            }
            function bump(Counter $c): Counter {
                return ++$c;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_CompoundAssign_OverloadReturn_AcceptedAsSelf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function accrue(Money $a): Money {
                $a += 5;
                return $a;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_BuiltinExtensionOperator_SelfReturn_ResolvesOutsideClass()
    {
        // Story 11 §8 fresh-review: the owning type for a builtin extension operator is a
        // BuiltInTypeSymbol, not an ObjectDeclarationSymbol — `self` in its return type must
        // resolve to that builtin even when the call site has no EnclosingObject (top-level
        // function), not report TYHP4064 / infer Unresolved.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOperators {
                operator *<string>(self $left, int $right): self {
                    return \str_repeat($left, $right);
                }
            }
            function bump(string $a): string {
                return $a * 3;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
    }

    [Fact]
    public void Check_BuiltinExtensionOperator_SelfReturn_ResolvesToBuiltinInsideUnrelatedClass()
    {
        // Fresh-review regression guard: before seeding EnclosingObjectType for the builtin owner,
        // a call site *inside* an unrelated class would silently pass the null-EnclosingObject
        // guard and then resolve `self` to that unrelated class (via ResolveEnclosingObjectType's
        // EnclosingObject fallback) instead of reporting the correct builtin return — a worse,
        // silent mis-inference (not just a spurious diagnostic).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOperators {
                operator *<string>(self $left, int $right): self {
                    return \str_repeat($left, $right);
                }
            }
            class Money {
                public int $amount = 0;
                function bumpWrong(string $a): Money {
                    return $a * 3;
                }
                function bumpRight(string $a): string {
                    return $a * 3;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().HaveCount(1,
            $"only `bumpWrong` (Money) should fail; `bumpRight` (string) must be accepted: "
            + string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NativePassthroughOverloadReturn_TypeAliasReturn_AcceptedAsSelf()
    {
        // Exercises a real bodyless (native-passthrough) tyhpdef overload on a class — PECL
        // decimal's `operator +(self, DecimalValue): self;` — plus return-type alias expansion
        // (`CompareResult = int`) through the same inference path.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function addDecimals(\Decimal\Decimal $a): \Decimal\Decimal {
                return $a + 1;
            }
            function compareDecimals(\Decimal\Decimal $a, \Decimal\Decimal $b): int {
                return $a <=> $b;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_NativeIntShift_StillInfersInt()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function shr(int $a): int {
                return $a >> 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_ThisInsideTrait_SingleComposingClass_AcceptedAsComposingReturn()
    {
        // Trait-$this is typed as the trait; composing-class search must still find Wallet's
        // operator + and infer its return (Wallet) when that is the sole matching user.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Adds {
                public function plus(Wallet $other): Wallet {
                    return $this + $other;
                }
            }
            class Wallet {
                use Adds;
                public int $amount = 0;
                operator +(self $left, self $right): self {
                    $left->amount = $left->amount + $right->amount;
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_ThisInsideTrait_SingleComposingClass_RejectedWhenWrong()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Adds {
                public function plus(Wallet $other): int {
                    return $this + $other;
                }
            }
            class Wallet {
                use Adds;
                public int $amount = 0;
                operator +(self $left, self $right): self {
                    $left->amount = $left->amount + $right->amount;
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_UnaryNot_ThisInsideTrait_SingleComposingClass_AcceptedAsComposingReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Negatable {
                public function invert(): Mask {
                    return ~$this;
                }
            }
            class Mask {
                use Negatable;
                public int $bits = 0;
                operator ~(self $value): self {
                    $value->bits = ~$value->bits;
                    return $value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_ThisInsideTrait_MultipleUsers_AgreeOnConcreteReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Adds {
                public function plusAmount(int $n): int {
                    return $this + $n;
                }
            }
            class Wallet {
                use Adds;
                public int $amount = 0;
                operator +(self $left, int $right): int {
                    return $left->amount + $right;
                }
            }
            class Purse {
                use Adds;
                public int $amount = 0;
                operator +(self $left, int $right): int {
                    return $left->amount + $right;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_ThisInsideTrait_MultipleUsers_DisagreeOnReturn_FallsBackPermissive()
    {
        // Wallet+:int vs Purse+:string — checker must not pick either; fall back to Unresolved
        // (permissive), so a wrong concrete return is not falsely rejected here.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Adds {
                public function plusAmount(int $n): string {
                    return $this + $n;
                }
            }
            class Wallet {
                use Adds;
                public int $amount = 0;
                operator +(self $left, int $right): int {
                    return $left->amount + $right;
                }
            }
            class Purse {
                use Adds;
                public int $amount = 0;
                operator +(self $left, int $right): string {
                    return (string)($left->amount + $right);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_BinaryAdd_ThisInsideTrait_MultipleUsers_SelfReturnsDisagree_FallsBackPermissive()
    {
        // Both declare `: self`, but resolved returns are Wallet vs Purse — disagreement.
        // Unresolved fallback must not invent Wallet (or Purse) and then reject `: self` on the
        // trait method (trait self is not a class subtype target for either user).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Adds {
                public function plus(self $other): self {
                    return $this + $other;
                }
            }
            class Wallet {
                use Adds;
                public int $amount = 0;
                operator +(self $left, self $right): self {
                    $left->amount = $left->amount + $right->amount;
                    return $left;
                }
            }
            class Purse {
                use Adds;
                public int $amount = 0;
                operator +(self $left, self $right): self {
                    $left->amount = $left->amount + $right->amount;
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
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
