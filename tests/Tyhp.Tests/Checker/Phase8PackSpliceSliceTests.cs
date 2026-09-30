using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.9 Phase 8: packs auto-splicing inside <c>callable&lt;&gt;</c>, postfix <c>T...</c>
/// (exact vs <c>extends</c>), <c>__CallableParametersSlice</c>, and the <c>array_map</c> zip /
/// <c>ClosureExtensions</c> consumers that depend on splice.
/// </summary>
[Trait("Category", "Checker")]
public class Phase8PackSpliceSliceTests
{
    [Fact]
    public void RestVariadicClosure_AssignableToSplicedCallable_ConcretePack()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable(__CallableParametersRest<callable(int, string): bool> ...): bool $cb): void {}
            function demo(): void {
                take(function (__CallableParametersRest<callable(int, string): bool> ...$args): bool {
                    return true;
                });
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RestVariadicClosure_SplicedArity_MatchesSourceCallable()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(): void {
                callable(__CallableParametersRest<callable(int, string): bool> ...): bool $cb =
                    function (__CallableParametersRest<callable(int, string): bool> ...$args): bool {
                        return true;
                    };
                $cb(1, 'x');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RestVariadicClosure_AssignableToSplicedCallable_UnboundGeneric()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function compose<TThisReturn, TNextCallable extends callable>(
                \Closure<callable(__CallableReturnType<TNextCallable>): TThisReturn> $current,
                \Closure<TNextCallable> $next
            ): \Closure<callable(__CallableParametersRest<TNextCallable> ...): TThisReturn> {
                return function (__CallableParametersRest<TNextCallable> ...$args) use ($current, $next): TThisReturn {
                    return $current($next(...$args));
                };
            }
            function demo(): void {
                \Closure<callable(int $i): string> $intToString = fn(int $x): string => (string)$x;
                \Closure<callable(string $s): bool> $stringToBool = fn(string $s): bool => $s !== '';
                \Closure<callable(int $i): bool> $composed = compose($stringToBool, $intToString);
                $composed(1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RestVariadicClosure_ReconstructsTypeParameter_MemoizePattern()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function memoize<TThisCallable extends callable>(
                \Closure<TThisCallable> $fn
            ): \Closure<TThisCallable> {
                return function (__CallableParametersRest<TThisCallable> ...$args) use ($fn): __CallableReturnType<TThisCallable> {
                    return $fn(...$args);
                };
            }
            function demo(): void {
                \Closure<callable(int $i): string> $intToString = fn(int $x): string => (string)$x;
                \Closure<callable(int $i): string> $memoized = memoize($intToString);
                $memoized(1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RestPack_SplicesToSourceArity_NotOneArgWrapper()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable(__CallableParametersRest<callable(int, string): bool> ...): bool $cb): void {
                $cb(1, 'x');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RestPack_WithPrefix_SplicesInPlace()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(
                callable(string, __CallableParametersRest<callable(int $i): bool> ...): mixed $cb
            ): void {
                $cb('x', 1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RestPack_WrongSplicedArity_Errors()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable(__CallableParametersRest<callable(int, string): bool> ...): bool $cb): void {
                $cb(1, 'x', true);
            }
            """);

        errors.Should().Contain(e =>
            e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Code == MessageCode.CheckerTypeMismatch
            || e.Code == MessageCode.CheckerMissingArgument
            || e.Code == MessageCode.CheckerTooManyArguments,
            Describe(errors));
    }

    [Fact]
    public void NullablePack_SplicesEachMemberAsNullable()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(
                callable(__Nullable<__CallableParametersRest<callable(int, string): bool>> ...): mixed $cb
            ): void {
                $cb(null, null);
                $cb(1, 'x');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NonPackNullable_StaysUnionWithNull()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function demo(__Nullable<int> $x): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "x");
        type.IsNullable.Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void NonNullablePack_StripsNullFromEachMember()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(
                callable(__NonNullable<__CallableParametersRest<callable(?int, ?string): mixed>> ...): mixed $cb
            ): void {
                $cb(1, 'x');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NonNullable_OfNull_StaysVoid_ForNonPack()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function demo(__NonNullable<null> $x): void {}
            """);

        UserErrors(diagnostics)
            .Where(e => e.Code != MessageCode.CheckerVoidNotAllowedHere)
            .Should()
            .BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "x");
        type.IsVoid.Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void DisplayName_OfSplicedCallable_ShowsSplicedForm_NotRestWrapper()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function demo(
                callable(__CallableParametersRest<callable(int, string): bool> ...): bool $cb
            ): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "cb");
        type.DisplayName.Should().NotContain("Rest", type.DisplayName);
        type.DisplayName.Should().Contain("int", type.DisplayName);
        type.DisplayName.Should().Contain("string", type.DisplayName);
        type.DisplayName.Should().Contain("bool", type.DisplayName);
    }

    [Fact]
    public void PackVariadicOnShape_Splices()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take<T extends callable>(
                callable(__CallableParametersRest<T> ...): bool $cb
            ): void {}
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    // ---- Postfix T... (homogeneous variadic) --------------------------------------------

    [Fact]
    public void PostfixEllipsis_Exact_ParsesAndAcceptsVariadicCallback()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable(string $s): int $x): void {}
            function demo(): void {
                callable(string ...): int $cb = fn(string ...$s): int => \count($s);
                $cb('a', 'b');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void PostfixEllipsis_Exact_RejectsNonVariadicSameArity()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(): void {
                callable(string ...): int $cb = fn(string $a, string $b): int => 0;
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void PostfixEllipsis_WithPrefix_Parses()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(): void {
                callable(int, bool, string ...): int $cb =
                    fn(int $num, bool $flag, string ...$values): int => \count($values);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void BareWildcard_StillAnyArity_AfterPostfixEllipsisAdded()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pin<T extends callable(...): bool>(T $cb): void {}
            function demo(): void {
                pin(fn(int $a, string $b): bool => true);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void PostfixEllipsis_AsTypeArgument_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(array<int...> $cb): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallablePostfixEllipsisNotAllowed,
            Describe(errors));
    }

    [Fact]
    public void PostfixEllipsis_NotLastParameter_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(callable(string ..., int): bool $cb): void {}
            """);

        errors.Should().NotBeEmpty(Describe(errors));
    }

    [Fact]
    public void PostfixEllipsis_OnArrayGeneric_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(array<int...> $xs): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallablePostfixEllipsisNotAllowed,
            Describe(errors));
    }

    [Fact]
    public void PostfixEllipsis_OnValueParameter_IsParseErrorOrRejected()
    {
        // `int... $x` is not the PHP variadic spelling (`int ...$x`); the postfix-ellipsis
        // grammar only exists inside `tyhpGenericTypeArgument` (callable(): mixed type-argument
        // lists), so this must not silently parse as a variadic parameter.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function f(int... $x): void {}
            """);

        errors.Should().NotBeEmpty("`int... $x` must not type-check as a valid variadic parameter");
    }

    [Fact]
    public void ExtendsBound_HomogeneousVariadic_WithoutRest_RequiresPhpVariadic()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function call_int_func<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback
            ): void {}
            function demo(): void {
                call_int_func(fn(int ...$xs): int => \count($xs));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtendsBound_HomogeneousVariadic_WithoutRest_RejectsFixedArity()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function call_int_func<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback
            ): void {}
            function demo(): void {
                call_int_func(fn(int $a, int $b): int => $a + $b);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    [Fact]
    public void ExtendsBound_HomogeneousVariadic_WithRest_AcceptsBothShapesForTwoArgs()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function call_int_user_func<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback,
                __CallableParametersRest<TCallableShape> ...$values
            ): TReturn {
                return $callback(...$values);
            }
            function demo(): void {
                call_int_user_func(fn(int ...$xs): int => \count($xs), 1, 2);
                call_int_user_func(fn(int $a, int $b): int => $a + $b, 1, 2);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtendsBound_HomogeneousVariadic_WithRest_RejectsMismatchedElementType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function call_int_user_func<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback,
                __CallableParametersRest<TCallableShape> ...$values
            ): TReturn {
                return $callback(...$values);
            }
            function demo(): void {
                call_int_user_func(fn(int $a, string $b): int => 0, 1, 'x');
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied
            || e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void ExtendsBound_PrefixPlusHomogeneousVariadic_WithoutRest_AcceptsVariadicTail()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function call_int_func<TReturn extends mixed|void|never, TCallableShape extends callable(bool, int ...): TReturn>(
                TCallableShape $callback
            ): void {}
            function demo(): void {
                call_int_func(fn(bool $flag, int ...$vals): int => \count($vals));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtendsBound_PrefixPlusHomogeneousVariadic_WithoutRest_RejectsExtraRequiredParam()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function call_int_func<TReturn extends mixed|void|never, TCallableShape extends callable(bool, int ...): TReturn>(
                TCallableShape $callback
            ): void {}
            function demo(): void {
                call_int_func(fn(bool $flag, int $a, int ...$rest): int => $a);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    [Fact]
    public void MixedEllipsisBound_DoesNotAcceptNarrowerParameter()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take<TCallableShape extends callable(mixed ...): mixed>(TCallableShape $cb): void {}
            function demo(): void {
                take(fn(int $x): int => $x);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    [Fact]
    public void AnyArityWildcardBound_StillAcceptsNarrowerParameter()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take<TCallableShape extends callable(...): mixed>(TCallableShape $cb): void {}
            function demo(): void {
                take(fn(int $x): int => $x);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    // ---- __CallableParametersSlice -------------------------------------------------------

    [Fact]
    public void Slice_NonVariadic_IsParameterAtStart()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function int_map<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $val
            ): TReturn {
                return $callback($val);
            }
            function demo(): int {
                return int_map(fn(int $a): int => $a, 1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Slice_TMin_OnNonVariadic_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function bad<TCallableShape extends callable>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0, 1> $val
            ): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableSliceMinOnFixed,
            Describe(errors));
    }

    [Fact]
    public void Slice_Variadic_FromStart_AcceptsNPlusArgs()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function int_map<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $val,
                __CallableParametersSlice<TCallableShape, 1> ...$vals
            ): TReturn {
                return $callback($val, ...$vals);
            }
            function demo(): int {
                return int_map(fn(int $a, int $b): int => $a + $b, 1, 2);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ArraySlice_IsArrayOfParameterAtIndex()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function take<TZip extends callable(int $i): mixed>(
                array<__CallableParametersSlice<TZip, 0>> $x
            ): void {}
            function demo(): void {
                take([1, 2, 3]);
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void SameFunctionSlices_Overlap_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function bad<TCallableShape extends callable>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $a,
                __CallableParametersSlice<TCallableShape, 0> ...$rest
            ): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableSliceOverlap,
            Describe(errors));
    }

    [Fact]
    public void SameFunctionSlices_Hole_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function bad<TCallableShape extends callable>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $a,
                __CallableParametersSlice<TCallableShape, 2> ...$rest
            ): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableSliceHole,
            Describe(errors));
    }

    [Fact]
    public void SameFunctionSlices_DisjointOrdered_NoHole_IsFine()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function ok<TCallableShape extends callable>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $a,
                __CallableParametersSlice<TCallableShape, 1> ...$rest
            ): void {}
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Slice_UnboundTCallable_StaysDeferred()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function demo<TCallable extends callable>(
                __CallableParametersSlice<TCallable, 0> $x
            ): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "x");
        type.DisplayName.Should().Contain("Slice", type.DisplayName);
    }

    // ---- int_map arity errors (4142/4143 family) -----------------------------------------

    [Fact]
    public void IntMap_CorrectArity_ReturnsInt()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function int_map<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $val,
                __CallableParametersSlice<TCallableShape, 1> ...$vals
            ): __CallableReturnType<TCallableShape> {
                return $callback($val, ...$vals);
            }
            function demo(): void {
                int $r = int_map(fn(int $a, int $b): int => $a + $b, 1, 2);
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void IntMap_ExtraArityBeyondRequiredParams_Errors()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function int_map<TReturn extends mixed|void|never, TCallableShape extends callable(int ...): TReturn>(
                TCallableShape $callback,
                __CallableParametersSlice<TCallableShape, 0> $val,
                __CallableParametersSlice<TCallableShape, 1> ...$vals
            ): TReturn {
                return $callback($val, ...$vals);
            }
            function demo(): void {
                int_map(fn(int $a, int $b): int => $a + $b, 1, 2, 3);
            }
            """);

        errors.Should().Contain(e =>
            e.Code == MessageCode.CheckerMissingArgument
            || e.Code == MessageCode.CheckerTooManyArguments
            || e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    // ---- array_map zip overlay -------------------------------------------------------------

    [Fact]
    public void ArrayMap_Zip_NullableCallback_AcceptsTwoArrays()
    {
        var errors = CompileWithStandardOverlay("""
            <?tyhp
            namespace Test;
            function demo(): void {
                \array_map(fn(?int $a, ?string $b): int => 0, [1], ['x']);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ArrayMap_Zip_NonNullableCallback_OnTwoArrays_Errors()
    {
        var errors = CompileWithStandardOverlay("""
            <?tyhp
            namespace Test;
            function demo(): void {
                \array_map(fn(int $a, string $b): int => 0, [1], ['x']);
            }
            """);

        errors.Should().NotBeEmpty(Describe(errors));
    }

    [Fact]
    public void ArrayMap_SingleArray_KeyPreservingOverload_StillUsed()
    {
        var errors = CompileWithStandardOverlay("""
            <?tyhp
            namespace Test;
            function demo(): void {
                \array_map(fn(int $a): int => $a * 2, [1, 2, 3]);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ArrayMap_InfersWithoutExplicitTypeArguments()
    {
        var errors = CompileWithStandardOverlay("""
            <?tyhp
            namespace Test;
            function demo(array<int> $ints, array<string> $strings): void {
                \array_map(fn(?int $a, ?string $b): int => 0, $ints, $strings);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    // ---- ClosureExtensions.compose / then --------------------------------------------------

    [Fact]
    public void ClosureExtensions_Compose_TypeChecksAsSplicedParams()
    {
        var errors = CompileWithCorePackage("""
            <?tyhp
            namespace Test;
            function demo(): void {
                \Closure<callable(int $i): string> $intToString = fn(int $x): string => (string)$x;
                \Closure<callable(string $s): bool> $stringToBool = fn(string $s): bool => $s !== '';
                \Closure<callable(int $i): bool> $composed = $stringToBool->compose($intToString);
                $composed(1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ClosureExtensions_Then_TypeChecksAsSplicedParams()
    {
        var errors = CompileWithCorePackage("""
            <?tyhp
            namespace Test;
            function demo(): void {
                \Closure<callable(int $i): string> $intToString = fn(int $x): string => (string)$x;
                \Closure<callable(string $s): bool> $stringToBool = fn(string $s): bool => $s !== '';
                \Closure<callable(int $i): bool> $then = $intToString->then($stringToBool);
                $then(1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ClosureExtensions_Memoize_TypeChecksAsSplicedParams()
    {
        var errors = CompileWithCorePackage("""
            <?tyhp
            namespace Test;
            function demo(): void {
                \Closure<callable(int $i): string> $intToString = fn(int $x): string => (string)$x;
                \Closure<callable(int $i): string> $memoized = $intToString->memoize();
                $memoized(1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ClosureExtensions_SourceFile_HasNoIncompatibleReturn()
    {
        var errors = CompileCorePackageExtensions();
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(errors.Where(e => e.Code == MessageCode.CheckerIncompatibleReturnType)));
    }

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var (_, _, _, diagnostics) = CompileForChecker(content);
        return UserErrors(diagnostics);
    }

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global, DiagnosticBag diagnostics) CompileForChecker(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            if (result.GlobalScope is null)
            {
                return (null!, result.ParsedFiles![0], null!, result.Diagnostics);
            }

            var symbolTree = new SymbolTree(result.GlobalScope);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope);
            checker.Check(result.ParsedFiles!);
            return (checker, result.ParsedFiles![0], result.GlobalScope, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // A small standalone stand-in for the `array_map` zip overloads in the real
    // `runtime/packages/php/_tyhpdef/overlays/Ext.Standard.tyhpdef` (kept in sync by hand).
    // C# unit tests must not load `runtime/packages`; see `IsolatedCompilation`. Live overlay
    // contracts are covered by `runtime/packages/test-all-tyhpdef.sh`.
    private const string ArrayMapZipOverlay = """
        <?tyhpdef
        // @overlay-against: function array_map(callable $callback, array $array, array $arrays): array
        function array_map<TKey extends int|string, TValue, TResult extends void|never|mixed>(
            callable(TValue): TResult $callback,
            array<TKey, TValue> $array
        ): array<TKey, TResult>;

        // zip operation when callback is null
        // @overlay-against: function array_map(callable $callback, array $array, array $arrays): array
        function array_map<TValue>(null $callback, array<TValue> $array, array<TValue> ...$arrays): array<int, array<int, TValue>>;

        // Overlay: extra-array zip uses Slice from index 1 (`TMin = 1`) so a one-array call
        // stays on the key-preserving overload. TZip is the non-null element schema; callback
        // parameters are a spliced `__Nullable` pack.
        // @overlay-against: function array_map(callable $callback, array $array, array $arrays): array
        function array_map<TZip extends callable>(
            callable(__Nullable<__CallableParametersRest<TZip>> ...): __CallableReturnType<TZip> $callback,
            array<__CallableParametersSlice<TZip, 0>> $array,
            array<__CallableParametersSlice<TZip, 1, 1>> ...$arrays
        ): array<int, __CallableReturnType<TZip>>;
        """;

    private static IReadOnlyList<IDiagnostic> CompileWithStandardOverlay(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);
        var overlayPath = Path.Combine(tempDir, "overlay.tyhpdef");
        File.WriteAllText(overlayPath, ArrayMapZipOverlay);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                skipChecking: true,
                configure: o => o.TyhpdefOverlayPaths = [overlayPath]);
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

    // A small standalone stand-in for the real `runtime/packages/core/tyhp_src/ClosureExtensions.tyhp`
    // (kept in sync by hand). C# unit tests must not load `runtime/packages`; see
    // `IsolatedCompilation`. Live extension contracts are covered by
    // `runtime/packages/test-all-tyhpdef.sh`.
    private const string ClosureExtensionsSource = """
        <?tyhp

        namespace Tyhp;

        global use extension \Tyhp\ClosureExtensions;

        extension ClosureExtensions {
            extends<TThisReturn, TNextCallable extends callable> \Closure<callable(__CallableReturnType<TNextCallable>): TThisReturn> {
                function compose(
                    \Closure<TNextCallable> $next
                ): \Closure<callable(__CallableParametersRest<TNextCallable> ...): TThisReturn> {
                    \Closure<callable(__CallableReturnType<TNextCallable>): TThisReturn> $current = $this;
                    return function (__CallableParametersRest<TNextCallable> ...$args) use ($current, $next): TThisReturn {
                        return $current($next(...$args));
                    };
                }
            }

            extends<TThisCallable extends callable> \Closure<TThisCallable> {

                function then<TNextReturn>(
                    \Closure<callable(__CallableReturnType<TThisCallable>): TNextReturn> $next
                ): \Closure<callable(__CallableParametersRest<TThisCallable> ...): TNextReturn> {
                    \Closure<TThisCallable> $current = $this;
                    return function (__CallableParametersRest<TThisCallable> ...$args) use ($current, $next): TNextReturn {
                        return $next($current(...$args));
                    };
                }

                function memoize(): \Closure<TThisCallable> {
                    \Closure<TThisCallable> $fn = $this;
                    array<string, __CallableReturnType<TThisCallable>> $cache = [];
                    return function (__CallableParametersRest<TThisCallable> ...$args) use ($fn, &$cache): __CallableReturnType<TThisCallable> {
                        string $key = \serialize($args);
                        if (!\array_key_exists($key, $cache)) {
                            $cache[$key] = $fn(...$args);
                        }
                        return $cache[$key];
                    };
                }
            }

            extends \Closure {

                function partial(mixed ...$partialArgs): \Closure {
                    \Closure $fn = $this;
                    return function (mixed ...$args) use ($fn, $partialArgs): mixed {
                        return $fn(...$partialArgs, ...$args);
                    };
                }
            }
        }
        """;

    private static IReadOnlyList<IDiagnostic> CompileWithCorePackage(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);
        var extensionsCopyPath = Path.Combine(tempDir, "ClosureExtensions.tyhp");
        File.WriteAllText(extensionsCopyPath, ClosureExtensionsSource);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath, extensionsCopyPath], options);
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

    private static IReadOnlyList<IDiagnostic> CompileCorePackageExtensions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var extensionsCopyPath = Path.Combine(tempDir, "ClosureExtensions.tyhp");
        File.WriteAllText(extensionsCopyPath, ClosureExtensionsSource);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([extensionsCopyPath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            return result.Diagnostics.Errors
                .Where(e => !(e.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ICheckedType ResolveParameterDeclaredType(TyhpChecker checker, SrcFileAst file, string parameterName)
    {
        var function = FindAllAst<PhpFunctionDeclAst>(file)
            .FirstOrDefault(decl => string.Equals(decl.Identifier, "demo", StringComparison.Ordinal));
        function.Should().NotBeNull("demo function should exist");

        var parameter = function!.Parameters?.GetAllNotNull()
            .FirstOrDefault(param =>
                string.Equals(param.Name.TrimStart('$'), parameterName, StringComparison.Ordinal));
        parameter.Should().NotBeNull($"parameter '{parameterName}' should exist");
        parameter!.Type.Should().NotBeNull();

        var state = new CheckerState { CurrentFileName = file.FileName };
        if (function.BoundSymbol is FunctionDeclarationSymbol functionSymbol)
        {
            state.EnclosingFunction = functionSymbol;
            if (functionSymbol.GenericParameters.Count > 0)
            {
                state.FunctionGenerics = functionSymbol.GenericParameters;
            }
        }

        return checker.ResolveTypeAnnotation(parameter.Type!, state);
    }

    private static IEnumerable<T> FindAllAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var found in FindAllAst<T>(child))
            {
                yield return found;
            }
        }
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
