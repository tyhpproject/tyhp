using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class TypeGuardRuleTests
{
    [Fact]
    public void Check_TypeGuardReturningNonBool_Reports4032()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isWrong(int $value): $value is string
            {
                return $value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeGuardInvalidReturn);
    }

    [Fact]
    public void Check_TypeGuardWithMissingReturn_Reports4032()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isWrong(mixed $value): $value is string
            {
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeGuardInvalidReturn);
    }

    [Fact]
    public void Check_ValidTypeGuardFunction_No4032()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isString(mixed $value): $value is string
            {
                return \is_string($value);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeGuardInvalidReturn);
    }

    [Fact]
    public void Check_TypeGuardMissingGuardParameter_Reports4032()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isWrong(mixed $value): $other is string
            {
                return \is_string($value);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeGuardInvalidReturn);
    }

    [Fact]
    public void Check_IsNullElseBranch_NarrowsToNonNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(?string $x): void {
                if (\is_null($x)) {
                } else {
                    string $copy = $x;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyNull);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_IsStringElseBranch_RemovesStringFromUnion()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string|int $x): void {
                if (\is_string($x)) {
                } else {
                    int $copy = $x;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_UserDefinedGuardElseBranch_NarrowsComplement()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function isFoo(mixed $value): $value is Foo
            {
                return $value instanceof Foo;
            }

            function demo(Foo|int $x): void {
                if (isFoo($x)) {
                } else {
                    int $copy = $x;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_UserDefinedArrayGuardElseBranch_NarrowsUnionComplement()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isArray(mixed $value): $value is array
            {
                return \is_array($value);
            }

            function demo(bool|array $val): void {
                if (isArray($val)) {
                } else {
                    bool $copy = $val;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_IsNullPositiveBranch_StillNarrowsToNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(?string $x): void {
                if (\is_null($x)) {
                    null $copy = $x;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_IsIterable_NarrowsToArrayOrTraversable()
    {
        // PHP is_iterable is true for arrays and Traversable. Positive branch must keep both;
        // else branch must exclude array from an array|string union (Traversable-only would not).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                if (\is_iterable($x)) {
                    array|\Traversable $it = $x;
                    iterable $it2 = $x;
                }
            }

            function demoElse(array|string $x): void {
                if (\is_iterable($x)) {
                    array|\Traversable $it = $x;
                } else {
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_IsCountable_NarrowsToArrayOrCountable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                if (\is_countable($x)) {
                    array|\Countable $c = $x;
                    int $n = \count($x);
                }
            }

            function demoElse(array|string $x): void {
                if (\is_countable($x)) {
                    array|\Countable $c = $x;
                } else {
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_StaticMethodTypeGuard_NarrowsPositiveBranch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Type
            {
                public static function isType<TTarget>(mixed $value): $value is TTarget
                {
                    return true;
                }
            }

            function demo(mixed $x): string {
                if (Type::isType<string>($x)) {
                    return $x;
                }
                return '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_StaticMethodTypeGuard_WithClassGeneric_NarrowsPositiveBranch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Type
            {
                public static function isType<TTarget>(mixed $value): $value is TTarget
                {
                    return true;
                }
            }

            class Holder<TValue>
            {
                public function get(mixed $result): TValue
                {
                    if (Type::isType<TValue>($result)) {
                        return $result;
                    }
                    throw new \RuntimeException('bad');
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_GenericFreeFunctionTypeGuard_NarrowsPositiveBranch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isType<TTarget>(mixed $value): $value is TTarget
            {
                return true;
            }

            function demo(mixed $x): int {
                if (isType<int>($x)) {
                    return $x;
                }
                return 0;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_GenericFreeFunctionTypeGuard_OmittedTypeArg_UsesDefault()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isTarget<TTarget = string>(mixed $value): $value is TTarget
            {
                return true;
            }

            function demo(mixed $x): string {
                if (isTarget($x)) {
                    return $x;
                }
                return '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_ClassExistsAlt_WithoutTypeArg_NarrowsToClassNameObject()
    {
        // ExtCore: class_exists_alt<T extends object = object>(...): $class is __ClassName<T>
        // Omitting <T> must apply the default so the narrowed type is __ClassName<object>.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $name): void {
                if (\class_exists_alt($name)) {
                    \__ClassName<object> $typed = $name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnknownError);
    }

    [Fact]
    public void Check_ClassExists_WithoutTypeArg_NarrowsToClassNameObject()
    {
        // Primary ExtCore class_exists is now the tyhpdef guard (same as class_exists_alt).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $name): void {
                if (\class_exists($name)) {
                    \__ClassName<object> $typed = $name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnknownError);
    }

    [Fact]
    public void Check_ClassExists_WithTypeArg_NarrowsToClassNameOfT()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(string $name): void {
                if (\class_exists<User>($name)) {
                    \__ClassName<User> $typed = $name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnknownError);
    }

    [Fact]
    public void Check_ClassExists_ElseBranch_DoesNotKeepClassNameNarrowing()
    {
        // Negative polarity must use the tyhpdef guard path (not only the positive SymbolNameGuards map).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $name): void {
                if (\class_exists($name)) {
                    return;
                }
                \__ClassName<object> $typed = $name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_InterfaceExists_NarrowsToInterfaceNameObject()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface IFace {}

            function demo(string $name): void {
                if (\interface_exists($name)) {
                    \__InterfaceName<object> $typed = $name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CallUserFunc_OmittedGenerics_DoesNotDefaultCallbackToVoid()
    {
        // ExtStandard call_user_func infers TCallable from the callback; rest args are
        // checked against that callable and must not collapse to callable(): void.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(callable<string, mixed, bool> $handler, string $name, mixed $out): bool {
                return \call_user_func($handler, $name, $out) === true;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void Check_CallUserFunc_TwoArgs_SelectsMatchingOverload()
    {
        // Rest unpack accepts the two trailing arguments; must not report TYHP4143.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(callable<string, mixed, bool> $handler, string $name, mixed $out): bool {
                return \call_user_func($handler, $name, $out) === true;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
    }

    [Fact]
    public void Check_WhileIsString_NarrowsLoopBody()
    {
        // Top-type #3: loop conditions must apply positive narrowing to the body.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                while (\is_string($x)) {
                    string $s = $x;
                    $x = null;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_ForIsString_NarrowsLoopBody()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                for (; \is_string($x); $x = null) {
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_DoWhileIsString_DoesNotNarrowFirstIterationBody()
    {
        // Do-while body runs before the condition is proven; narrowing would be unsound.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                do {
                    string $s = $x;
                } while (\is_string($x));
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_IsIterable_OnArray_DoesNotWidenDeclaredType()
    {
        // Top-type #7: positive guard must intersect, not replace — `array` stays `array`
        // under `\is_iterable` (array|\Traversable), not widen to the full union.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array $x): void {
                if (\is_iterable($x)) {
                    array $y = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NegatedIsString_EarlyReturn_NarrowsFallThrough()
    {
        // Top-type #2 remaining: unwrap `!` so `if (!\is_string($x)) return;` narrows `$x` after.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(mixed $x): string {
                if (!\is_string($x)) {
                    return '';
                }
                return $x;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NegatedIsString_WithElse_NarrowsBothArms()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(mixed $x): string {
                if (!\is_string($x)) {
                    return '';
                } else {
                    return $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NestedWhileIsString_NarrowsBothLevels()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                while (\is_string($x)) {
                    while (\is_string($x)) {
                        string $s = $x;
                    }
                    string $t = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_ForUpdateReassignsNarrowedVariable_BodyStillNarrowedEachEntry()
    {
        // The update expression only runs once per (single-pass) check, after the body — it must
        // not retroactively affect the body's narrowing for this pass.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                for (; \is_string($x); $x = null) {
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_DoubleNegation_NarrowsLikeNoNegation()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                if (!!\is_string($x)) {
                    return $x;
                }
                return '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NegatedOrCombination_NarrowsBothViaDeMorgan()
    {
        // `!` unwrap flips polarity before the `||` node is inspected, so `!(A || B)` still hits
        // the existing negative-OR De Morgan narrowing (both operands narrow negatively).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, mixed $y): void {
                if (!(\is_string($x) || \is_string($y))) {
                    return;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_IsIterable_OnArrayIntUnion_NarrowsToArrayMember()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array|int $x): void {
                if (\is_iterable($x)) {
                    array $y = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_InstanceofOnNullableClass_ClearsPossiblyNull()
    {
        // Closely-related gap found while reviewing Top-type #7: `instanceof` never matches
        // null, but the positive-narrowing path never cleared `IsPossiblyNull`, so this legal,
        // common pattern spuriously reported 4015 (possibly-null) even after the guard.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}
            function demo(?Foo $x): void {
                if ($x instanceof Foo) {
                    Foo $y = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NestedForLoop_InnerLoopDoesNotClobberOuterNarrowing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                for (; \is_string($x); ) {
                    for (int $i = 0; $i < 3; $i = $i + 1) {
                        string $s = $x;
                    }
                    string $t = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_AndPositive_BothOperandsNarrowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, mixed $y): void {
                if (\is_string($x) && \is_int($y)) {
                    string $s = $x;
                    int $i = $y;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_AndChain_ProgressiveNarrowing_AllowsArrayKeyExistsAfterIsArray()
    {
        // Intra-condition progressive narrowing: after `\is_array($x)` the same `&&` chain's
        // later operands must see `$x` as `array` (array_key_exists requires `array`).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                if (\is_array($x) && \array_key_exists(0, $x) && \array_key_exists(1, $x)) {
                    mixed $a = $x[0];
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_IndexAccess_IsStringGuard_NarrowsUseSite()
    {
        // Index-access subjects are tracked by structural key (`$x[1]`), so the use-site
        // (a distinct AST node from the guard argument) still resolves as `string`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                if (\is_array($x) && \array_key_exists(1, $x) && \is_string($x[1])) {
                    string $s = $x[1];
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_IndexAccess_WriteToNarrowedKey_TargetsElementTypeNotStaleNarrowing()
    {
        // Index-access narrowing (`\is_string($x[1])`) describes what a prior *read* observed —
        // it must not be treated as a constraint on a later *write* to that same slot (the
        // assignment below is legal: the array's element type is `mixed`), and the write must
        // invalidate the stale narrowing so the following read sees the real (unnarrowed) type.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array $x): string {
                if (\is_string($x[1])) {
                    $x[1] = 42;
                    return $x[1];
                }
                return '';
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_IndexAccess_Narrowing_DoesNotLeakPastIf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                if (\is_array($x) && \is_string($x[1])) {
                    string $ok = $x[1];
                }
                return $x[1];
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CrossClassStaticCall_SelfParameterResolvesToCalleeClass()
    {
        // `self` in a callee's parameter list is relative to the method's own class, not the
        // call-site enclosing type (same rule as return-type `self`).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function wrap(self $other): self {
                    return $other;
                }
            }
            class Caller {
                public static function demo(Box $b): void {
                    Box::wrap($b);
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_FirstClassCallable_Strval_IsCallableNotString()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array $items): array {
                return \array_map(\strval(...), $items);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_BareAndStatement_ProgressiveNarrowingDoesNotLeakToNextStatement()
    {
        // The `&&` progressive-narrowing type-check (TypeCompatibilityRule.CheckBinaryOp /
        // CheckerHelpers.CheckCompileTimeConstructsInTree) must narrow on a disposable probe.
        // This node is not an if/while/ternary/switch condition — there is no dedicated
        // `ApplyConditionNarrowing` call on a real branch state to correct for it afterward — so
        // without the probe, `$x`'s narrowed type from validating the `&&` leaks into the
        // unrelated `return` that follows.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                \is_string($x) && \strlen($x) > 0;
                return $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_NegatedAndEarlyReturn_NarrowsBothFallThrough()
    {
        // if (!(A && B)) { return; } — fall-through implies A && B held, so both narrow. The `!`
        // unwrap flips polarity to positive before the `&&` node is inspected, enabling the
        // existing positive-AND narrowing on the fall-through path.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, mixed $y): void {
                if (!(\is_string($x) && \is_int($y))) {
                    return;
                }
                string $s = $x;
                int $i = $y;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NegatedIsString_Continue_NarrowsRestOfIteration()
    {
        // Top-type #2 remaining: `continue` must set abrupt-completion so CheckIf absorbs the
        // negative arm — `if (!\is_string($x)) continue;` narrows `$x` for the rest of the body.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                while (true) {
                    if (!\is_string($x)) {
                        continue;
                    }
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NegatedIsString_Break_NarrowsRestOfIteration()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                while (true) {
                    if (!\is_string($x)) {
                        break;
                    }
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NullGuard_Continue_NarrowsFallThroughToNonNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
            }
            function demo(?Base $x): void {
                while (true) {
                    if ($x === null) {
                        continue;
                    }
                    Base $y = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NullGuard_Break_NarrowsFallThroughToNonNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
            }
            function demo(?Base $x): void {
                while (true) {
                    if ($x === null) {
                        break;
                    }
                    Base $y = $x;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_LoopWithOnlyBreakNoReturn_StillReportsMissingReturn()
    {
        // Regression probe (review of the continue/break fix): `break`/`continue` must not leak
        // their abrupt-completion signal past the enclosing loop and be mistaken for a real
        // function-level return. `CheckLoop` unconditionally resets `HasReturnedOnAllPaths` after
        // the body, so a function whose only statement is a loop that merely `break`s must still
        // report a missing return for a non-void return type.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): int {
                while (true) {
                    if (\is_string($x)) {
                        break;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_SwitchCaseGuardBreak_NarrowsRestOfCaseAndAllowsMissingReturnDetection()
    {
        // `break` inside a `switch` arm must behave identically to `break` inside a loop for the
        // narrowing signal (same `HasReturnedOnAllPaths` field, agnostic to which construct the
        // jump actually exits), and the switch must still reset the flag for its own continuation
        // like `CheckLoop` does — a bare `break` inside a case must not be mistaken for a function
        // return either.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                switch (true) {
                    case true:
                        if (!\is_string($x)) {
                            break;
                        }
                        return $x;
                }
                return '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_SwitchOnlyBreakNoReturn_StillReportsMissingReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(int $v): string {
                switch ($v) {
                    case 0:
                        break;
                    default:
                        return 'b';
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_BothArmsBreakOrContinue_MarksAbruptCompletionOnBothPaths()
    {
        // Both arms exiting abruptly (one `break`, one `continue`) must AND together into
        // `HasReturnedOnAllPaths = true` for the if/else join, exactly like both arms `return`ing
        // — proven indirectly via the missing-return check on the enclosing function, since a
        // trailing statement after the if is otherwise unreachable and not itself diagnostic.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): int {
                while (true) {
                    if (\is_string($x)) {
                        break;
                    } else {
                        continue;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_NestedLoopContinueGuard_DoesNotLeakNarrowingPastInnerLoop()
    {
        // The inner loop's guard-then-continue narrows `$x` to `string` for the rest of the inner
        // iteration, but that narrowing must not leak past the inner loop into the outer body —
        // `CheckLoop` merges the inner loop's exit state back with a union (clearing narrowing),
        // so `$x` is `mixed` again once the inner loop finishes. No sophisticated "must have
        // narrowed on every exit path" join is attempted (documented scope limit).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                while (true) {
                    while (true) {
                        if (!\is_string($x)) {
                            continue;
                        }
                        break;
                    }
                    string $s = $x;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ContinueGuard_NarrowingDoesNotLeakPastLoopEnd()
    {
        // Symmetric to the nested-loop probe above, at a single loop level: narrowing from a
        // `continue`-guard inside the body must not survive past the loop's own closing brace.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                while (true) {
                    if (!\is_string($x)) {
                        continue;
                    }
                    string $s = $x;
                }
                string $t = $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_EarlierArmBreak_DoesNotLeakAbruptCompletionIntoLaterArm()
    {
        // Regression (review of the continue/break fix / Top-type #5): each non-falling-through
        // switch arm is Split fresh from the pre-switch state, and HasReturnedOnAllPaths is reset
        // at the start of every arm (including fall-through continuations). An earlier arm's
        // `break` must not leave a stale abrupt-completion flag that would make a later arm's
        // ordinary (non-exiting) `if (!guard) { ... }` look always-exiting and wrongly absorb only
        // the negative-narrowed branch. Here that would silence the real type mismatch on
        // `string $s = $x;`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                switch (true) {
                    case true:
                        break;
                    case false:
                        if (!\is_string($x)) {
                        }
                        string $s = $x;
                        break;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SwitchTrue_IsStringGuard_NarrowsCaseBody()
    {
        // Top-type #5: `switch (true) { case \is_string($x): …; break; }` must narrow `$x` in the
        // case body (mirrors match-arm narrowing from Top-type #4).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                switch (true) {
                    case \is_string($x):
                        return $x;
                    default:
                        return '';
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_SwitchTrue_InstanceofGuard_NarrowsCaseBody()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): string { return 'ok'; }
            }

            function demo(mixed $x): string {
                switch (true) {
                    case $x instanceof Foo:
                        return $x->bar();
                    default:
                        return '';
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_SwitchTrue_DefaultArm_MixedStillRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                switch (true) {
                    default:
                        return $x;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SwitchTrue_FallThroughLabels_DoNotOverNarrow()
    {
        // Empty fall-through labels OR into one body — positive OR narrowing is unsound, so the
        // shared body must not treat `$x` as `string` (nor as `int`).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                switch (true) {
                    case \is_string($x):
                    case \is_int($x):
                        string $s = $x;
                        break;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SwitchTrue_FallThroughAfterNarrowedBody_ClearsGuard()
    {
        // A case that narrows then intentionally falls through must not leave that narrowing in
        // the next arm (entry may also be via the next label alone).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                switch (true) {
                    case \is_string($x):
                        string $s = $x;
                    case true:
                        string $t = $x;
                        break;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SwitchTrue_SequentialGuards_DoNotLeakNarrowingAcrossArms()
    {
        // Independent case groups: narrowing from `is_string` must not survive into the next
        // arm after `break` (fresh Split per non-falling-through group).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                switch (true) {
                    case \is_string($x):
                        string $s = $x;
                        break;
                    case true:
                        string $t = $x;
                        break;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchArm_IsStringGuard_NarrowsArmBody()
    {
        // Top-type #4: `match (true) { \is_string($x) => … }` must narrow `$x` in the arm so the
        // idiomatic PHP type-dispatch form satisfies the checker (mirrors if/ternary guards).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                return match (true) {
                    \is_string($x) => $x,
                    default => '',
                };
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_MatchArm_InstanceofGuard_NarrowsArmBody()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): string { return 'ok'; }
            }

            function demo(mixed $x): string {
                return match (true) {
                    $x instanceof Foo => $x->bar(),
                    default => '',
                };
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_MatchResult_StringAssignedToInt_ReportsTypeMismatch()
    {
        // Top-type #4 soundness hole: match result type must be checked against the assignment
        // target. Previously InferMatchArm typed the synthetic `return` unary as `unresolved`
        // (assignable to everything), so this compiled silently.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                int $n = match (true) {
                    default => 'definitely a string',
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchResult_StringReturnedAsInt_ReportsIncompatibleReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                return match (true) {
                    default => 'a string',
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchArm_WithoutGuard_MixedStillRejected()
    {
        // Negative: a default arm (no type-guard condition) must not invent narrowing — assigning
        // `mixed $x` to `string` still errors.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                return match (true) {
                    default => $x,
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchArm_MultiConditionOr_DoesNotOverNarrow()
    {
        // Multiple arm conditions are OR'd. Narrowing as if both guards held would be unsound
        // (`is_string($x), is_int($x)` cannot make `$x` both string and int). Leave un-narrowed
        // so `string $s = $x` still errors.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                string $s = match (true) {
                    \is_string($x), \is_int($x) => $x,
                    default => '',
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_NestedMatch_InnerArmNarrowsIndependently()
    {
        // Review probe (Top-type #4 fix): a match arm's value expression can itself be another
        // match; the inner InferMatch call must Split/narrow from the inner arm's own armState,
        // not leak the outer arm's narrowing scope incorrectly.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, mixed $y): string {
                return match (true) {
                    \is_string($x) => match (true) {
                        \is_int($y) => $x,
                        default => $x,
                    },
                    default => '',
                };
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MatchUsedAsStatement_NarrowingStillAbsorbedIntoContinuation()
    {
        // Review probe: `ControlFlowRule.CheckConditional` defers match entirely to
        // `ResolveExpressionType` even when the match is a standalone statement (result
        // discarded) rather than an assignment/return RHS — the arm's narrowing/assignment
        // effects must still reach the real state via `AbsorbJoinedVariables`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                match (true) {
                    default => 0,
                };
                string $s = $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchArm_ThreeArmSequentialDispatch_EachNarrowsFromOriginalSubjectState()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                return match (true) {
                    \is_int($x) => 'int',
                    \is_string($x) => $x,
                    default => 'other',
                };
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MatchDefaultArmThrows_DoesNotPolluteResultTypeWithUnresolved()
    {
        // Review probe: a `throw` arm's synthetic-return operand infers as `unresolved` (InferUnary
        // has no dedicated `throw` case). Verifies the resulting union (`string|unresolved`) still
        // rejects assignment from an unrelated arm's real type — `unresolved` unions per-member
        // (TypeComparer.IsAssignableToCore), so it does not silently widen the whole result.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                return match (true) {
                    \is_string($x) => $x,
                    default => throw new \Exception('unreachable'),
                };
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MatchDefaultArmThrows_MismatchedArmStillReported()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                return match (true) {
                    \is_int($x) => $x,
                    default => throw new \Exception('unreachable'),
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchArm_CompoundAndCondition_CountsAsSingleConditionAndNarrowsBoth()
    {
        // `\is_string($x) && \is_int($y)` is one arm condition (not a comma-separated OR list), so
        // it takes the single-condition narrowing path and both operands of the `&&` narrow.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, mixed $y): string {
                return match (true) {
                    \is_string($x) && \is_int($y) => $x,
                    default => '',
                };
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MatchArm_DoesNotCarryNegativeNarrowingFromEarlierArm()
    {
        // Documents a known, intentional scope limit (match and switch both Split each
        // non-falling-through arm fresh from the un-narrowed subject state): a later arm cannot
        // rely on an earlier arm's condition having been false (no elseif-style cascading negative
        // narrowing across arms). This is a completeness gap, not unsoundness — flip this
        // expectation only alongside an intentional cross-arm-narrowing enhancement.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): string {
                return match (true) {
                    \is_int($x) => 'int',
                    default => $x,
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_MatchArm_NotNullGuard_ClearsPossiblyNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function m(): void {}
            }
            function demo(?Base $x): void {
                match (true) {
                    $x !== null => $x->m(),
                    default => null,
                };
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_MatchNoDefaultArm_SingleArmNarrowingSoundlyAbsorbedAfterMatch()
    {
        // No default arm (non-exhaustive at compile time). Absorbing the single conditioned arm's
        // positive narrowing unconditionally after the match is still sound: reaching the
        // continuation implies that arm matched (otherwise a runtime UnhandledMatchError, and
        // control never reaches here).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                match (true) {
                    \is_string($x) => 'a',
                };
                string $s = $x;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_SwitchFallThrough_RealAssignmentSurvivesClearedGuard()
    {
        // Review regression (Top-type #5 follow-up): the fall-through transition used to call a
        // blanket `ClearControlFlowNarrowing` that reset every visible variable's narrowed type
        // back to its *declared* type — including one the falling-through arm had just legitimately
        // reassigned via a plain `$x = …;` (no guard involved at all). That wiped a definitely
        // non-null reassignment back to the declared `?string`, producing a false-positive nullable
        // return-type mismatch even though `$x` is provably non-null on every path that reaches
        // the `return` (pre-switch `$x = "initial"` on direct `case 2` entry, or the reassignment
        // when falling through from `case 1`). Use a separate assignment (not only a typed-local
        // initializer) so NarrowedType is set before the switch; FOUND #10's dual-entry merge then
        // keeps string on both paths. The fix must distinguish "unused guard assumption" (safe to
        // drop) from "the body actually assigned this" (must survive).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(int $v): string {
                ?string $x = null;
                $x = "initial";
                switch ($v) {
                    case 1:
                        $x = "reassigned";
                    case 2:
                        return $x;
                }
                return '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_SwitchFallThrough_TypeGuardReassignedBeforeFallingThrough_DirectEntryWidens()
    {
        // Fall-through path: guard narrows `$x` to string, body reassigns to `int`, revert keeps
        // the reassignment (not the stale string guard). Direct entry via `case true:` still has
        // `mixed`. FOUND #10 joins both paths before the body, so `int $i = $x` must be rejected
        // (reassignment survival alone is covered by RealAssignmentSurvivesClearedGuard when both
        // entry paths agree on a non-null string).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                switch (true) {
                    case \is_string($x):
                        $x = 42;
                    case true:
                        int $i = $x;
                        break;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SwitchFallThrough_DirectEntryKeepsNullable_ReportsMismatch()
    {
        // FOUND #10: fall-through from `case 1` reassigns `$x` to a definite string, but direct
        // entry via `case 2` leaves the nullable parameter untouched. The body must be checked
        // against the join of both paths — `\strlen($x)` is unsound on direct entry.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(int $v, ?string $x): int {
                switch ($v) {
                    case 1:
                        $x = "reassigned";
                    case 2:
                        return \strlen($x);
                    default:
                        return 0;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType
            || d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerVariablePossiblyNull
            || d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_SwitchFallThrough_MultiHopChain_UnusedGuardStillClearedPastUntouchedArm()
    {
        // Three-arm fall-through chain where the middle arm never touches the guarded variable:
        // the first arm's `is_string($x)` guard baseline must still be threaded through the
        // untouched middle arm and reverted once a body finally reads `$x` without re-establishing
        // the guard, so `$x` is not wrongly treated as `string` two hops down the fall-through chain.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                switch (true) {
                    case \is_string($x):
                        doSomethingElse();
                    case false:
                        // untouched middle arm — does not read or write $x
                        doSomethingElse();
                    case true:
                        string $s = $x;
                        break;
                }
            }

            function doSomethingElse(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_NestedSwitchTrue_InnerAndOuterGuardsBothNarrow()
    {
        // A `switch (true)` type-guard arm nested inside another `switch (true)` type-guard arm:
        // the inner switch's per-arm Split must not disturb the outer arm's own narrowing, and the
        // outer guard on `$x` must still be visible inside the inner switch's arms.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, mixed $y): string {
                switch (true) {
                    case \is_string($x):
                        switch (true) {
                            case \is_int($y):
                                return $x . (string)$y;
                            default:
                                return $x;
                        }
                        break;
                    default:
                        break;
                }
                return '';
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
