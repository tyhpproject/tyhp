using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

// Story 11 §8 redesign: every operator overload collapses into a single STATIC method (except
// convert's instance to-forms). Call sites become static calls (`\Type::__add($a, $b)`), casts
// become instance convert-to calls (`$a->__toInt()`), and convert-to overloads auto-add the
// matching `\Tyhp\Contracts\*Convertible` interface.
[Trait("Category", "Emitter")]
public class OperatorOverloadEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var (php, _) = CompileAndEmitWithDiagnostics(tyhp, allowEmitterPostfixSplitErrors: false);
        return php;
    }

    private static (string Php, DiagnosticBag Diagnostics) CompileAndEmitWithDiagnostics(
        string tyhp,
        bool allowEmitterPostfixSplitErrors = true)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "operators.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                // Compound-assign type checking does not yet model operator-overload expansion for
                // every LHS shape; it is irrelevant to what the emitter produces.
                .Where(d => d.Code != MessageCode.CheckerTypeMismatch)
                .Where(d => !allowEmitterPostfixSplitErrors
                    || d.Code != MessageCode.EmitterPostfixOperatorOverloadRequiresStatementSplit)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            return (php, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }

    [Fact]
    public void Emit_BinaryAdd_RewritesToStaticAdd()
    {
        var php = CompileAndEmit("""
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

        php.Should().Contain("\\Money::__add($a, 10)");
        php.Should().Contain("public static function __add(");
        php.Should().NotContain("$a->__add(");
        php.Should().NotContain("__addThisTo");
    }

    [Fact]
    public void Emit_BinaryAdd_RewritesWhenOperandsInferredFromNew()
    {
        // Regression: `$a = new Money(...)` must register an object-typed local so `$a + $b`
        // rewrites even without a declared parameter/typed-var type.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public function __construct(
                    public readonly int $amount,
                    public readonly string $currency
                ) {}
                operator +(self $left, self $right): self {
                    return new self($left->amount + $right->amount, $left->currency);
                }
            }
            function demo(): void {
                $a = new Money(100, "USD");
                $b = new Money(50, "USD");
                $c = $a + $b;
            }
            """);

        php.Should().Contain("\\Money::__add($a, $b)");
        php.Should().NotContain("$c = ($a + $b)");
        php.Should().NotContain("$c = $a + $b");
    }

    [Fact]
    public void Emit_RightOperandOverload_RewritesToStaticAddWithOriginalOrder()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
                operator +(int $left, self $right): int {
                    return $left + $right->amount;
                }
            }
            function addLeft(Money $a): int {
                return 10 + $a;
            }
            """);

        // Right-operand resolution keeps operand order: `10 + $a` → `\Money::__add(10, $a)`.
        php.Should().Contain("\\Money::__add(10, $a)");
        php.Should().NotContain("__addThisTo");
        php.Should().NotContain("10 + $a;");
    }

    [Fact]
    public void Emit_Comparison_RewritesToStaticIsLessThan()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator <(self $left, self $right): bool {
                    return $left->amount < $right->amount;
                }
            }
            function cheaper(Money $a, Money $b): bool {
                return $a < $b;
            }
            """);

        php.Should().Contain("\\Money::__isLessThan($a, $b)");
        php.Should().Contain("public static function __isLessThan(");
    }

    [Fact]
    public void Emit_Comparison_RewritesToStaticIsGreaterThan()
    {
        // Regression: bare `>` must parse as an overloadable op (ExtDecimal audit §1 / FOUND_BUGS).
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator >(self $left, self $right): bool {
                    return $left->amount > $right->amount;
                }
            }
            function dearer(Money $a, Money $b): bool {
                return $a > $b;
            }
            """);

        php.Should().Contain("\\Money::__isGreaterThan($a, $b)");
        php.Should().Contain("public static function __isGreaterThan(");
    }

    [Fact]
    public void Emit_ShiftRight_StillDistinctFromGreaterThan()
    {
        var php = CompileAndEmit("""
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

        php.Should().Contain("\\Bits::__bwSR($a, 1)");
        php.Should().Contain("public static function __bwSR(");
        php.Should().NotContain("__isGreaterThan");
    }

    [Fact]
    public void Emit_Spaceship_RewritesToStaticCompare()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator <=>(self $left, self $right): int {
                    return $left->amount <=> $right->amount;
                }
            }
            function cmp(Money $a, Money $b): int {
                return $a <=> $b;
            }
            """);

        php.Should().Contain("\\Money::__compare($a, $b)");
        php.Should().Contain("public static function __compare(");
        php.Should().NotContain("__spaceship");
    }

    [Fact]
    public void Emit_BooleanNot_RewritesToStaticNot()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Flag {
                public bool $on = false;
                operator !(self $value): bool {
                    return !$value->on;
                }
            }
            function invert(Flag $a): bool {
                return !$a;
            }
            """);

        php.Should().Contain("\\Flag::__not($a)");
        php.Should().Contain("public static function __not(");
    }

    [Fact]
    public void Emit_Empty_DeclaresStaticIsEmptyAndGuardsCallSite()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Bag {
                public int $count = 0;
                operator empty(self $o): bool {
                    return $o->count == 0;
                }
            }
            function isBlank(Bag $b): bool {
                return empty($b);
            }
            """);

        // Declaration: `operator empty` collapses to a static `__isEmpty(self $o): bool`.
        php.Should().Contain("public static function __isEmpty(self $o): bool");
        // Call site: `empty($b)` keeps PHP's native short-circuit, then delegates to the operator.
        // Outer parens are required so the `||` does not bind tighter than surrounding operators.
        php.Should().Contain("(empty($b) || \\Bag::__isEmpty($b))");
        php.Should().NotContain("return empty($b);");
    }

    [Fact]
    public void Emit_Increment_RewritesSimplePropertyToAssignment()
    {
        // `$holder->money++` must write the incremented value back (same as `$m++`), not emit a
        // discarded bare `\Money::__increment(...)` call.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator ++(self $value): self {
                    $value->amount = $value->amount + 1;
                    return $value;
                }
            }
            class Holder {
                public Money $money;
            }
            function bump(Holder $h): void {
                $h->money++;
            }
            """);

        php.Should().Contain("$h->money = \\Money::__increment($h->money)");
        php.Should().Contain("public static function __increment(");
        php.Should().NotContain("$h->money++;");
    }

    [Fact]
    public void Emit_Increment_NonSimpleLhs_ExtractsTempWithStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator ++(self $value): self {
                    $value->amount = $value->amount + 1;
                    return $value;
                }
            }
            class Box {
                public Money $money;
            }
            class Outer {
                public Box $box;
            }
            function bump(Outer $o): void {
                $o->box->money++;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__increment(");
        php.Should().NotContain("$o->box->money++;");
    }

    [Fact]
    public void Emit_PostfixIncrement_AsValue_CapturesOldValue()
    {
        // `$b = $a++` must yield the prior value (PHP postfix), not the write-back result.
        var php = CompileAndEmit("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
            }
            function takeOld(Counter $a): Counter {
                $b = $a++;
                return $b;
            }
            """);

        php.Should().Contain("__old");
        php.Should().Contain("\\Counter::__increment(");
        php.Should().MatchRegex(@"\$__old_\d+\s*=\s*\$a");
        php.Should().MatchRegex(@"\$a\s*=\s*\\Counter::__increment\(\$a\)");
        php.Should().MatchRegex(@"\$b\s*=\s*\$__old_\d+");
        php.Should().NotContain("$b = $a++");
        php.Should().NotContain("$b = ($a = \\Counter::__increment($a))");
    }

    [Fact]
    public void Emit_PostfixDecrement_AsValue_CapturesOldValue()
    {
        // `$b = $a--` must yield the prior value, symmetric with postfix `++`.
        var php = CompileAndEmit("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator --(self $value): self {
                    $value->n = $value->n - 1;
                    return $value;
                }
            }
            function takeOld(Counter $a): Counter {
                $b = $a--;
                return $b;
            }
            """);

        php.Should().Contain("__old");
        php.Should().Contain("\\Counter::__decrement(");
        php.Should().MatchRegex(@"\$__old_\d+\s*=\s*\$a");
        php.Should().MatchRegex(@"\$a\s*=\s*\\Counter::__decrement\(\$a\)");
        php.Should().MatchRegex(@"\$b\s*=\s*\$__old_\d+");
        php.Should().NotContain("$b = $a--");
        php.Should().NotContain("$b = ($a = \\Counter::__decrement($a))");
    }

    [Fact]
    public void Emit_PrefixIncrement_AsValue_YieldsNewValue()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
            }
            function takeNew(Counter $a): Counter {
                $b = ++$a;
                return $b;
            }
            """);

        php.Should().Contain("$b = ($a = \\Counter::__increment($a))");
        php.Should().NotContain("__old");
    }

    [Fact]
    public void Emit_PostfixIncrement_NonSimpleLhs_AsValue_UsesTempAndOld()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator ++(self $value): self {
                    $value->amount = $value->amount + 1;
                    return $value;
                }
            }
            class Box {
                public Money $money;
            }
            class Outer {
                public Box $box;
            }
            function takeOld(Outer $o): Money {
                $b = $o->box->money++;
                return $b;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("__old");
        php.Should().Contain("\\Money::__increment(");
        php.Should().NotContain("$b = $o->box->money++");
    }

    [Fact]
    public void Emit_PostfixIncrement_InTernaryArm_ReportsTYHP5019()
    {
        var (php, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
            }
            function pick(bool $cond, Counter $a, Counter $other): Counter {
                return $cond ? $a++ : $other;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.EmitterPostfixOperatorOverloadRequiresStatementSplit);
        _ = php;
    }

    [Fact]
    public void Emit_PostfixIncrement_InElseIfCondition_ReportsTYHP5019()
    {
        // Regression: `else if` attaches the nested `if` directly (no wrapping block), so its
        // condition is reached only when every preceding condition in the chain was false.
        // Hoisting a postfix split before the whole chain would run it unconditionally, so this
        // must be diagnosed like a ternary arm — not silently emit `($a = __increment($a)) > 0`
        // (new-value semantics) with no error.
        var (_, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
                operator >(self $left, int $right): bool {
                    return $left->n > $right;
                }
            }
            function pick(Counter $a, int $flag): int {
                if ($flag === 1) {
                    return 1;
                } else if ($a++ > 0) {
                    return 2;
                }
                return 3;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.EmitterPostfixOperatorOverloadRequiresStatementSplit);
    }

    [Fact]
    public void Emit_PostfixIncrement_InPlainIfCondition_HoistsOldValue()
    {
        // A plain `if` condition (no elseif chain) is evaluated exactly once, so it is safe to
        // hoist — unlike the `elseif` case above.
        var (php, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
                operator >(self $left, int $right): bool {
                    return $left->n > $right;
                }
            }
            function pick(Counter $a): int {
                if ($a++ > 0) {
                    return 2;
                }
                return 3;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.EmitterPostfixOperatorOverloadRequiresStatementSplit);
        php.Should().Contain("__old");
        php.Should().MatchRegex(@"if\s*\(\$__old_\d+\s*>\s*0\)");
    }

    [Fact]
    public void Emit_PostfixIncrement_InWhileCondition_ReportsTYHP5019()
    {
        // A `while` condition re-evaluates every iteration, so the postfix split cannot be
        // hoisted once before the loop.
        var (_, diagnostics) = CompileAndEmitWithDiagnostics("""
            <?tyhp
            class Counter {
                public int $n = 0;
                operator ++(self $value): self {
                    $value->n = $value->n + 1;
                    return $value;
                }
                operator >(self $left, int $right): bool {
                    return $left->n > $right;
                }
            }
            function drain(Counter $a): void {
                while ($a++ > 0) {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.EmitterPostfixOperatorOverloadRequiresStatementSplit);
    }

    [Fact]
    public void Emit_BinaryAdd_RewritesWhenRightOperandIsIntProperty()
    {
        // Regression: `$money + $box->count` must still match `(self, int)` even though a naive
        // name-guess of `$box->count` would fall through to the Box receiver type.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            class Box {
                public int $count = 0;
            }
            function addCount(Money $m, Box $b): Money {
                return $m + $b->count;
            }
            """);

        php.Should().Contain("\\Money::__add($m, $b->count)");
        php.Should().NotContain("return $m + $b->count");
    }

    [Fact]
    public void Emit_CompoundAssign_ExpandsToStaticAssignment()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function grow(Money $a): void {
                $a += 10;
            }
            """);

        php.Should().Contain("$a = \\Money::__add($a, 10)");
        php.Should().NotContain("$a += 10");
    }

    [Fact]
    public void Emit_ConvertTo_ParameterNamedThis_DoesNotReassignThis()
    {
        // Regression: convert-to is always an instance method, so a self-operand literally named
        // `$this` already *is* PHP's real `$this` — the alias line must be skipped. Emitting
        // `$this = $this;` is a PHP fatal error ("Cannot re-assign $this").
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $this): int {
                    return $this->amount;
                }
            }
            """);

        php.Should().Contain("return $this->amount;");
        php.Should().NotContain("$this = $this;");
    }

    [Fact]
    public void Emit_Cast_RewritesToInstanceToInt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function asInt(Money $a): int {
                return (int)$a;
            }
            """);

        // convert-to is ALWAYS an instance method.
        php.Should().Contain("$a->__toInt()");
        php.Should().Contain("public function __toInt(): int");
        php.Should().Contain("implements \\Tyhp\\Contracts\\IntConvertible");
        php.Should().NotContain("(int)$a");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_CallArgument_RewritesToInstanceToInt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function takeInt(int $n): void {}
            function pass(Money $a): void {
                takeInt($a);
            }
            """);

        php.Should().Contain("takeInt($a->__toInt())");
        php.Should().NotContain("takeInt($a);");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_NamedCallArgument_RewritesToInstanceToInt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function takeInt(int $n): void {}
            function pass(Money $a): void {
                takeInt(n: $a);
            }
            """);

        php.Should().Contain("$a->__toInt()");
        php.Should().NotContain("takeInt(n: $a);");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_ConstructorArgument_RewritesToInstanceToInt()
    {
        // `new Type(...)` is a distinct AST node from a call (`PhpDereferenceableAst`/`PhpCallAst`),
        // so it needs its own hook into the implicit-convert rewrite against `__construct`'s params.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            class Wallet {
                public int $balance;
                function __construct(int $balance) {
                    $this->balance = $balance;
                }
            }
            function pass(Money $m): Wallet {
                return new Wallet($m);
            }
            """);

        php.Should().Contain("new Wallet($m->__toInt())");
        php.Should().NotContain("new Wallet($m)");
    }

    [Fact]
    public void Emit_ImplicitConvertFrom_ConstructorArgument_RewritesToStaticFrom()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            class Wallet {
                public Money $balance;
                function __construct(Money $balance) {
                    $this->balance = $balance;
                }
            }
            function pass(int $n): Wallet {
                return new Wallet($n);
            }
            """);

        php.Should().Contain("new Wallet(\\Money::__from($n))");
        php.Should().NotContain("new Wallet($n)");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_Return_RewritesToInstanceToInt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function asInt(Money $a): int {
                return $a;
            }
            """);

        php.Should().Contain("return $a->__toInt();");
        php.Should().NotContain("return $a;");
    }

    [Fact]
    public void Emit_ImplicitConvertFrom_CallArgument_RewritesToStaticFrom()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            function takeMoney(Money $m): void {}
            function pass(int $n): void {
                takeMoney($n);
            }
            """);

        php.Should().Contain("takeMoney(\\Money::__from($n))");
        php.Should().NotContain("takeMoney($n);");
    }

    [Fact]
    public void Emit_ImplicitConvertFrom_Return_RewritesToStaticFrom()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            function asMoney(int $n): Money {
                return $n;
            }
            """);

        php.Should().Contain("return \\Money::__from($n);");
        php.Should().NotContain("return $n;");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_ExtensionCallArgument_RewritesToInstanceToInt()
    {
        // Extension calls rewrite to a static call with the receiver prepended — the implicit
        // convert must still match against the *extension method's* declared parameter (index 1,
        // after the prepended receiver), not the original (pre-rewrite) call shape.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            extension StringExtensions {
                function repeat(extends string $this, int $times): string {
                    return \str_repeat($this, $times);
                }
            }
            function pass(string $s, Money $m): string {
                return $s->repeat($m);
            }
            """);

        php.Should().Contain("\\StringExtensions::repeat($s, $m->__toInt())");
        php.Should().NotContain("\\StringExtensions::repeat($s, $m)");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_NullSafeExtensionCallArgument_RewritesToInstanceToInt()
    {
        // Null-safe extension calls rewrite to a ternary wrapping the static call — the implicit
        // convert must reach into that wrapped call, not just the plain `PhpDereferenceableAst` case.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            extension StringExtensions {
                function repeat(extends string $this, int $times): string {
                    return \str_repeat($this, $times);
                }
            }
            function pass(?string $s, Money $m): ?string {
                return $s?->repeat($m);
            }
            """);

        php.Should().Contain("\\StringExtensions::repeat(");
        php.Should().Contain("$m->__toInt()");
        php.Should().NotContain("repeat($__recv, $m)");
    }

    [Fact]
    public void Emit_ConvertFrom_IsStaticFromMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            """);

        php.Should().Contain("public static function __from(int $from): self");
    }

    [Fact]
    public void Emit_Declaration_CollapsesFormsIntoOneStaticMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    return $left;
                }
                operator +(int $left, self $right): int {
                    return $left;
                }
            }
            """);

        // Exactly one collapsed static __add with union operand types + internal dispatch.
        php.Should().Contain("public static function __add(");
        php.Should().Contain("$l instanceof self");
        php.Should().Contain("\\is_int(");
        php.Should().Contain("InvalidParametersForOperatorOverloadException");
        // Old design artifacts are gone.
        php.Should().NotContain("__addThisTo");
        php.Should().NotContain("__OP_");
    }

    [Fact]
    public void Emit_CollapsedForms_KeepDistinctBodiesWhenParamNamesReused()
    {
        // Regression: reused param names across collapsed forms must not cross-pollute operand
        // type resolution (inner `int + int` stays plain, not a spurious static call).
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
                operator +(int $left, self $right): int {
                    return $left + $right->amount;
                }
            }
            """);

        php.Should().Contain("$left->amount = ($left->amount + $right)");
        php.Should().Contain("return $left + $right->amount");
        php.Should().NotContain("\\Money::__add($left->amount");
    }

    [Fact]
    public void Emit_CompoundAssign_NonSimpleLhs_ExtractsTempWithStaticCall()
    {
        // `$o->box->money` is non-simple (receiver is not a plain variable), so the emitter must
        // bind a by-ref temp instead of doubling the LHS evaluation, calling the static method.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            class Box {
                public Money $money;
            }
            class Outer {
                public Box $box;
            }
            function bump(Outer $o): void {
                $o->box->money += 10;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().NotContain("$o->box->money += 10");
    }

    [Fact]
    public void Emit_CompoundAssign_ArrayElementLhs_ExtractsTempWithStaticCall()
    {
        // `$arr[$i]` is non-simple and must resolve the element type from `array<Money>` so the
        // overload rewrite + by-ref temp extraction can run (not leave `$arr[$i] += 10`).
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function bump(array<Money> $arr, int $i): void {
                $arr[$i] += 10;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().NotContain("$arr[$i] += 10");
    }

    [Fact]
    public void Emit_CompoundAssign_ArrayElementLhs_KeyedArray_ExtractsTempWithStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function bump(array<string, Money> $arr, string $key): void {
                $arr[$key] += 10;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().NotContain("$arr[$key] += 10");
    }

    [Fact]
    public void Emit_CompoundAssign_ArrayElementLhs_WithIndexSideEffect_ExtractsTempOnce()
    {
        // Index `$i++` must not be evaluated twice — temp bind uses `&$arr[$i++]` once.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function bump(array<Money> $arr, int $i): void {
                $arr[$i++] += 10;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().Contain("$arr[$i++]");
        php.Should().NotContain("$arr[$i++] += 10");
    }

    [Fact]
    public void Emit_CompoundAssign_NestedArrayElementLhs_PeelsOneLevelPerIndex()
    {
        // `array<array<Money>>` — `$arr[$i][$j]` must peel one generic-arg level per index
        // (not stop after the outer `array<...>` or mis-resolve to the outer element type).
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function bump(array<array<Money>> $arr, int $i, int $j): void {
                $arr[$i][$j] += 10;
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().NotContain("$arr[$i][$j] += 10");
    }

    [Fact]
    public void Emit_BinaryAdd_ArrayElement_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function addAt(array<Money> $arr, int $i): Money {
                return $arr[$i] + 10;
            }
            """);

        php.Should().Contain("\\Money::__add($arr[$i], 10)");
        php.Should().NotContain("return $arr[$i] + 10");
    }

    [Fact]
    public void Emit_BinaryAdd_DoesNotRewriteScalarPropertyAccess()
    {
        // Regression: `$left->amount + $right` must stay as int addition inside an operator body
        // (property type is int, not the enclosing Money receiver).
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            """);

        php.Should().Contain("$left->amount + $right");
        php.Should().NotContain("$left->amount->__add(");
        php.Should().NotContain("\\Money::__add($left->amount");
    }

    [Fact]
    public void Emit_StandaloneExtensionOperator_CompilesCleanAndEmitsStaticMethod()
    {
        // Regression: ExtensionRule must seed EnclosingObject before checking standalone
        // extension operators — otherwise `self` return/operands fail with TYHP4064 and
        // CompileAndEmit rejects the file before emit.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators {
                operator +<Money>(self $left, self $right): self {
                    return $left;
                }
            }
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("class MoneyOperators");
        php.Should().Contain("function __add");
        php.Should().Contain("\\MoneyOperators::__add($a, $b)");
    }

    [Fact]
    public void Emit_StandaloneExtensionOperator_BuiltinStringTarget_RewritesCallSite()
    {
        // Docs: extension StringOperators { operator *<string>(self $left, int $right) }
        // and `'-' * 40` must rewrite to \StringOperators::__mul('-', 40).
        var php = CompileAndEmit("""
            <?tyhp
            extension StringOperators {
                operator *<string>(self $left, int $right): string {
                    return \str_repeat($left, $right);
                }
            }
            function dashes(int $n): string {
                return '-' * $n;
            }
            """);

        php.Should().Contain("class StringOperators");
        php.Should().Contain("function __multiply");
        php.Should().Contain("\\StringOperators::__multiply('-', $n)");
    }

    [Fact]
    public void Emit_StandaloneExtensionUnaryOperator_BuiltinIntTarget_RewritesCallSite()
    {
        // Unary form of the builtin-target fix: operator -<int>(self $v) must rewrite `-$n` to a
        // static call, exercising AliasConverter.TryFindMatchingUnaryOverload's builtin branch.
        var php = CompileAndEmit("""
            <?tyhp
            extension IntOperators {
                operator -<int>(self $v): int {
                    return 0 - $v;
                }
            }
            function negate(int $n): int {
                return -$n;
            }
            """);

        php.Should().Contain("class IntOperators");
        php.Should().Contain("function __negate");
        php.Should().Contain("\\IntOperators::__negate($n)");
    }

    [Fact]
    public void Emit_StandaloneExtensionOperator_BuiltinStringTarget_DoesNotRewriteMismatchedOperands()
    {
        // Regression guard: `self` on a builtin target must only match that specific builtin —
        // int operands must not accidentally pick up a `<string>`-targeted overload.
        var php = CompileAndEmit("""
            <?tyhp
            extension StringOperators {
                operator *<string>(self $left, int $right): string {
                    return \str_repeat($left, $right);
                }
            }
            function product(int $a, int $b): int {
                return $a * $b;
            }
            """);

        php.Should().Contain("$a * $b");
        php.Should().NotContain("StringOperators::__multiply");
    }

    [Fact]
    public void Emit_CompoundAssign_ThisScalarProperty_RewritesToStaticCall()
    {
        // `$this->balance += 10` must resolve `$this` via the enclosing class stack.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            class Wallet {
                public Money $balance = new Money();
                public function deposit(int $n): void {
                    $this->balance += $n;
                }
            }
            """);

        php.Should().Contain("$this->balance = \\Money::__add($this->balance, $n)");
        php.Should().NotContain("$this->balance += $n");
    }

    [Fact]
    public void Emit_BinaryAdd_ThisScalarProperty_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            class Wallet {
                public Money $balance = new Money();
                public function peek(): Money {
                    return $this->balance + 10;
                }
            }
            """);

        php.Should().Contain("\\Money::__add($this->balance, 10)");
        php.Should().NotContain("return $this->balance + 10");
    }

    [Fact]
    public void Emit_CompoundAssign_ThisArrayElementProperty_ExtractsTempWithStaticCall()
    {
        // `$this->items[$i] += 10` needs both `$this` resolution and array-element typing.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            class Wallet {
                public array<Money> $items = [];
                public function bump(int $i): void {
                    $this->items[$i] += 10;
                }
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().NotContain("$this->items[$i] += 10");
    }

    [Fact]
    public void Emit_CompoundAssign_ThisNestedProperty_ExtractsTempWithStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            class Box {
                public Money $money = new Money();
            }
            class Outer {
                public Box $box = new Box();
                public function bump(): void {
                    $this->box->money += 10;
                }
            }
            """);

        php.Should().Contain("__tmp");
        php.Should().Contain("\\Money::__add(");
        php.Should().NotContain("$this->box->money += 10");
    }

    [Fact]
    public void Emit_BinaryAdd_ThisAsDirectOperand_RewritesToStaticCall()
    {
        // `$this` itself (not a property access) as a direct operand — exercises the
        // `_classStack`-backed `$this` special-case in `ResolveOperatorExpressionType` without
        // going through the property/array-element resolver branches.
        var php = CompileAndEmit("""
            <?tyhp
            class Wallet {
                public int $amount = 0;
                operator +(self $left, self $right): self {
                    $left->amount = $left->amount + $right->amount;
                    return $left;
                }
                public function plus(self $other): self {
                    return $this + $other;
                }
            }
            """);

        php.Should().Contain("\\Wallet::__add($this, $other)");
        php.Should().NotContain("return $this + $other");
    }

    [Fact]
    public void Emit_BinaryAdd_ThisAsDirectOperand_InsideTrait_RewritesViaStaticLateBinding()
    {
        // Trait method bodies are walked with `_classStack` = the trait. Direct-operand `$this`
        // must still find `operator +` on the composing class and emit `static::__add` so the
        // shared trait body late-binds per user. Return type is the composing class (not trait
        // `self`) so checker overload-return inference agrees with emit.
        var php = CompileAndEmit("""
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

        php.Should().Contain("static::__add($this, $other)");
        php.Should().NotContain("return $this + $other");
        php.Should().NotContain("\\Adds::__add");
    }

    [Fact]
    public void Emit_UnaryNot_ThisAsDirectOperand_InsideTrait_RewritesViaStaticLateBinding()
    {
        // Expression-statement form keeps the fixture focused on emit rewrite (no return check).
        var php = CompileAndEmit("""
            <?tyhp
            trait Negatable {
                public function touch(): void {
                    ~$this;
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

        php.Should().Contain("static::__bwNot($this)");
        php.Should().NotContain("~$this;");
        php.Should().NotContain("\\Negatable::__bwNot");
    }

    [Fact]
    public void Emit_ImplicitConvertTo_ReturnOfThis_InsideTrait_RewritesToInstanceToInt()
    {
        // Same composing-class gap as the explicit-cast fix, but for the implicit convert-to
        // path used at call/return/`new` sites (`TryRewriteImplicitConvert`): `return $this;`
        // inside a trait method with `: int` must find `operator convert` on the composing
        // class, not just the trait itself.
        var php = CompileAndEmit("""
            <?tyhp
            trait Convertible {
                public function toInt(): int {
                    return $this;
                }
            }
            class Money {
                use Convertible;
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            """);

        php.Should().Contain("return $this->__toInt();");
        php.Should().NotContain("return $this;");
    }

    [Fact]
    public void Emit_BinaryAdd_ThisAsDirectOperand_InsideTrait_MultipleUsers_StillUsesStatic()
    {
        // Multiple users with agreeing concrete return (`int`) — emit still uses `static::`.
        // (Users that only agree on `self` resolve to distinct class types and are a checker
        // disagreement case; emit still late-binds via `static` from the first probe hit.)
        var php = CompileAndEmit("""
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

        php.Should().Contain("static::__add($this, $n)");
        php.Should().NotContain("return $this + $n");
    }
}
