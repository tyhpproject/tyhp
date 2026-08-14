using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regression tests for call/method return-type inference and builtin-type normalization.
/// Each scenario previously produced a spurious checker error (mostly TYHP4008/4009/4043)
/// because the inferrer fell back to <c>mixed</c>/<c>unknown</c> or because builtin types were
/// compared with inconsistent namespace qualification (e.g. <c>void</c> vs <c>\void</c>).
/// </summary>
[Trait("Category", "Checker")]
public class CallReturnTypeInferenceTests
{
    [Fact]
    public void StaticMethodCall_ReturnTypeInferred_NoError()
    {
        // `Box::make()` is parsed with a class-constant-access suffix; the inferrer must still
        // resolve it as a static method and surface the declared return type instead of `mixed`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public static function make(): Box {
                    return new Box();
                }
                public function rebuild(): Box {
                    return Box::make();
                }
            }
            """);

        errors.Should().BeEmpty(
            $"static method calls should infer their return type: {Describe(errors)}");
    }

    [Fact]
    public void InstanceMethodCall_OnNullableReceiver_ReturnTypeInferred_NoError()
    {
        // Member access on a nullable receiver (`?Node`) must still resolve the method by
        // unwrapping the nullable layer; otherwise the call inferred to `mixed`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Node {
                private ?Node $next = null;
                public function peek(): ?Node {
                    return $this->next;
                }
                public function chain(): ?Node {
                    return $this->next->peek();
                }
            }
            """);

        errors.Should().BeEmpty(
            $"instance calls on nullable receivers should infer their return type: {Describe(errors)}");
    }

    [Fact]
    public void SelfStaticMethodCall_AndNewSelf_ReturnTypeInferred_NoError()
    {
        // `self::create()` and `new self()` reference the class by a bare name the binder does
        // not bind; the inferrer must resolve them via the enclosing object type.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Factory {
                public static function create(): self {
                    return new self();
                }
                public static function build(): self {
                    return self::create();
                }
            }
            """);

        errors.Should().BeEmpty(
            $"self:: calls and new self() should infer their return type: {Describe(errors)}");
    }

    [Fact]
    public void StaticPropertyArrayAccess_ElementTypeInferred_NoError()
    {
        // `self::$items[$key]` reads through a static property whose member name is a variable
        // token; the inferrer must resolve the property and then the array element type.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Registry {
                private static array<string, Registry> $items = [];
                public static function get(string $key): Registry {
                    return self::$items[$key];
                }
            }
            """);

        errors.Should().BeEmpty(
            $"static-property array access should infer the element type: {Describe(errors)}");
    }

    [Fact]
    public void StaticSingletonCoalesceAssign_ReturnTypeInferred_NoError()
    {
        // The full singleton-factory pattern: `self::$cache[$k] ??= new self()` previously
        // inferred `mixed|unknown` because both the array access and `new self()` failed.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Singleton {
                private static array<string, Singleton> $cache = [];
                public static function of(string $key): Singleton {
                    return self::$cache[$key] ??= new self();
                }
            }
            """);

        errors.Should().BeEmpty(
            $"singleton coalesce-assign factory should infer Singleton: {Describe(errors)}");
    }

    [Fact]
    public void VoidMethodWithBareReturn_NormalizesAgainstDeclaredVoid_NoError()
    {
        // A declared `: void` resolves to the nominal `\void`, while a bare `return;` infers the
        // `void` singleton; the two encodings must compare equal.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Sink {
                public function maybe(bool $flag): void {
                    if ($flag) {
                        return;
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"void vs \\void should normalize: {Describe(errors)}");
    }

    [Fact]
    public void BoolReturningMethodCall_UsedAsCondition_NormalizesBool_NoError()
    {
        // A `bool`-returning method call resolves to the nominal `\bool`; the condition check must
        // treat that as the builtin `bool`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Guard {
                public function ready(): bool {
                    return true;
                }
                public function run(): void {
                    if ($this->ready()) {
                        return;
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerConditionNotBool,
            $"\\bool condition should normalize to bool: {Describe(errors)}");
    }

    [Fact]
    public void StaticMethodCall_WrongReturnType_StillReportsError()
    {
        // Guard: the inference improvements must not mask genuine return-type mismatches.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public static function make(): Box {
                    return new Box();
                }
                public function bad(): string {
                    return Box::make();
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            "a static call returning Box must not satisfy a string return type");
    }

    [Fact]
    public void GenericReceiver_PropertyTypedAsClassParam_SubstitutesCallSiteArg()
    {
        // Promise-style: class param is TReturn, call site uses Promise<T>, so $p->value must be T.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<TItem> {
                public TItem $value;
                public function __construct(TItem $value): void {
                    $this->value = $value;
                }
            }
            function unwrap<T>(Box<T> $box): T {
                return $box->value;
            }
            """);

        errors.Should().BeEmpty(
            $"generic receiver property access should substitute class type args: {Describe(errors)}");
    }

    [Fact]
    public void GenericReceiver_PropertyAccess_InsideClassWithDifferentFunctionGeneric_Substitutes()
    {
        // Mirrors Promise::_await<T>(Promise<T>): T returning $promise->value (typed TReturn).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Promise<TReturn> {
                private TReturn $value;
                public static function await<T>(Promise<T> $promise): T {
                    return $promise->value;
                }
            }
            """);

        errors.Should().BeEmpty(
            $"Promise<T>->$value should be T, not unbound TReturn: {Describe(errors)}");
    }

    /// <summary>
    /// FOUND_BUGS item 39: an explicit call-site type argument on a generic static method must
    /// fully substitute the method's own type parameter in the return type — including when that
    /// parameter shadows a same-named class type parameter (Fiber::suspend&lt;TResume&gt;).
    /// </summary>
    [Fact]
    public void GenericStaticMethod_CallSiteTypeArg_SubstitutesShadowingMethodParam()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Fiber<TStart = mixed, TResume = mixed, TReturn = mixed, TSuspend = mixed> {
                public static function suspend<TResume = mixed>(mixed $value = null): ?TResume {
                    return null;
                }
            }
            class Promise {
                public static function await<T>(): T {
                    return Fiber::suspend<T>(null) ?? self::missing<T>();
                }
                private static function missing<T>(): T {
                    throw new \Exception('missing');
                }
            }
            """);

        errors.Should().BeEmpty(
            $"Fiber::suspend<T> should return ?T (not TResume|T): {Describe(errors)}");
    }

    [Fact]
    public void GenericStaticMethod_CallSiteTypeArg_WithoutClassShadowing_Substitutes()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public static function identity<T>(T $value): T {
                    return $value;
                }
            }
            class Caller {
                public static function run<U>(U $value): U {
                    return Box::identity<U>($value);
                }
            }
            """);

        errors.Should().BeEmpty(
            $"Box::identity<U> should return U: {Describe(errors)}");
    }

    [Fact]
    public void GenericInstanceMethod_CallSiteTypeArg_SubstitutesReturnType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public function identity<T>(T $value): T {
                    return $value;
                }
            }
            class Caller {
                public static function run<U>(Box $box, U $value): U {
                    return $box->identity<U>($value);
                }
            }
            """);

        errors.Should().BeEmpty(
            $"$box->identity<U> should return U: {Describe(errors)}");
    }

    [Fact]
    public void CallablePropertyInvokedDirectly_ReturnTypeInferred_NotSwallowedAsUnresolved()
    {
        // `$this->formatter(5)` calls a property holding a `\Closure<...>` directly (no
        // `__invoke`/parens). It parses with the same instance-member-access + call shape as a
        // real method call, so the "resolve named method callees first" reordering added for
        // FOUND_BUGS item 39 must still fall back to the property's callable type — rather than an
        // unconditional `unknown` — when the member isn't a method.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public \Closure<int, string> $formatter;
                public function __construct(\Closure<int, string> $formatter): void {
                    $this->formatter = $formatter;
                }
                public function run(): int {
                    return $this->formatter(5);
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"a string-returning closure property call must not satisfy an int return type: {Describe(errors)}");
    }

    [Fact]
    public void NewSelfWithTypeArg_SubstitutesConstructorParameter()
    {
        // FOUND #16 async / relative-types audit: `new self<T>($fn)` must type the constructor's
        // `callable<TReturn>` as `callable<T>`, not leave class-level `TReturn` unbound against the
        // method generic. Factories use `: self<T>` (parameterized `static<…>` is forbidden).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn extends void|mixed = void> {
                public function __construct(callable<TReturn> $executor): void {}
                public static function async<T extends void|mixed>(callable<T> $fn): self<T> {
                    return new self<T>($fn);
                }
            }
            """);

        errors.Should().BeEmpty(
            $"new self<T>($fn) must accept callable<T>: {Describe(errors)}");
    }

    [Fact]
    public void ReflectionClass_StaticTypeArg_IsAllowed()
    {
        // Nested bare `static` inside a generic type argument is not "static as a property type".
        // `self<T>::class` brands as `__ClassName<Promise<T>>`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn extends void|mixed = void> {
                private static ?\ReflectionClass $cached = null;
                private static function settled<T extends void|mixed>(): self<T> {
                    self::$cached ??= new \ReflectionClass<self<T>>(self<T>::class);
                    return self::$cached->newInstanceWithoutConstructor();
                }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerStaticNotReturnType);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerParameterizedStaticForbidden);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ReflectionClass_StaticTypeArg_OnProperty_IsAllowed()
    {
        // Nested `static` inside a property type is not "static as a property type"
        // (bare `static $x` is already rejected by `typeWithoutStatic` in the grammar).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                private ?\ReflectionClass<static> $ref = null;
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerStaticNotReturnType);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void ClosureArgument_PreservesCapturedVariableTypes()
    {
        // FOUND #16 async: ValidateArgumentTypes must not Split(AnonymousFunction) before CheckNode,
        // or `use ($boxes)` captures lose their types and foreach yields mixed.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<T> {
                public function __construct(public T $value): void {}
            }
            function take(callable $fn): void {}
            function run<T>(array<Box<T>> $boxes): void {
                take(function () use ($boxes): void {
                    foreach ($boxes as $box) {
                        Box<T> $ok = $box;
                    }
                });
            }
            """);

        errors.Should().BeEmpty(
            $"closure use ($boxes) must keep array<Box<T>> element type: {Describe(errors)}");
    }

    [Fact]
    public void NullableTypeParam_AssignsToUnionIncludingNull()
    {
        // FOUND #16 async: `?T` must assign to `T|U|null` (Deferred.resolve → doResolve).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Deferred<T extends void|mixed = void> {
                public function resolve(?T $value = null): void {
                    $this->take($value);
                }
                private function take(T|string|null $value): void {}
            }
            """);

        errors.Should().BeEmpty(
            $"?T must assign to T|string|null: {Describe(errors)}");
    }

    [Fact]
    public void GenericInstantiation_WithConstructorArguments_TypesAsTheClass()
    {
        // `new Box<int>(5)` is ambiguous with the comparison chain `(new Box) < int > (5)`.
        // When the comparison reading wins, the expression types as `bool` and the return
        // is rejected, so this guards the semantics rather than just the parse.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<T> {
                public function __construct(?T $v = null): void {}
            }
            class Factory {
                public static function make(): Box<int> {
                    return new Box<int>(5);
                }
            }
            """);

        errors.Should().BeEmpty(
            $"new Box<int>(5) should type as Box<int>, not a comparison: {Describe(errors)}");
    }

    [Fact]
    public void TypedClassConstant_IsInferredAsItsDeclaredType()
    {
        // A typed class constant (`const string TAG`) carries its type onto the constant symbol, so
        // `Widget::TAG` types as `string`. Returning it where `int` is declared must be rejected;
        // while the type was dropped the constant inferred as `mixed` and this passed silently.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Widget {
                public const string TAG = 'w';
                public function tag(): int {
                    return Widget::TAG;
                }
            }
            """);

        errors.Should().ContainSingle(
            $"a string-typed constant is not an int: {Describe(errors)}")
            .Which.Code.Should().Be(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void TypedClassConstant_UsedAtItsDeclaredType_NoError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Widget {
                public const string TAG = 'w';
                public const ?int COUNT = null;
                public function tag(): string {
                    return Widget::TAG;
                }
                public function count(): ?int {
                    return Widget::COUNT;
                }
            }
            """);

        errors.Should().BeEmpty(
            $"typed constants should satisfy a matching declared return type: {Describe(errors)}");
    }

    [Fact]
    public void ArrayMap_InfersTResultFromCallbackReturn_AssignableToTypedArray()
    {
        // FOUND Story 11 §4 — callable<TValue, TResult> must unify TResult from the arrow return.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function mapNames(array<string> $names): array<int> {
                return \array_map(
                    fn(string $n): int => \strlen($n),
                    $names
                );
            }
            """);

        errors.Should().BeEmpty(
            $"array_map should infer TResult from the callback return: {Describe(errors)}");
    }

    [Fact]
    public void ArrayMap_InfersTValueAndTResult_ForObjectElementCallback()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            class Node {}
            class Packed {
                public function __construct(public string $label): void {}
            }

            function pack(array<Node> $nodes): array<Packed> {
                return \array_map(
                    fn(Node $n): Packed => new Packed('x'),
                    $nodes
                );
            }
            """);

        errors.Should().BeEmpty(
            $"array_map should infer both TValue and TResult: {Describe(errors)}");
    }

    [Fact]
    public void ArrayMap_WrongCallbackReturn_StillReportsTypeMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function bad(array<string> $names): array<int> {
                return \array_map(
                    fn(string $n): string => $n,
                    $names
                );
            }
            """);

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void GenericInstanceMethod_ArgumentInference_SubstitutesReturnType()
    {
        // CHECKER_GAPS P1 #14: method-call argument-driven inference (parity with free functions).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public function identity<T>(T $value): T {
                    return $value;
                }
            }
            function demo(Box $box): void {
                string $s = $box->identity('hello');
            }
            """);

        errors.Should().BeEmpty(
            $"method generic inference should type identity's return as string: {Describe(errors)}");
    }

    [Fact]
    public void GenericStaticMethod_ArgumentInference_SubstitutesReturnType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Id {
                public static function of<T>(T $value): T {
                    return $value;
                }
            }
            function demo(): void {
                string $s = Id::of('hello');
            }
            """);

        errors.Should().BeEmpty(
            $"static method generic inference should type of's return as string: {Describe(errors)}");
    }

    [Fact]
    public void GenericFunction_ReturnTypeWithKeyParam_NoFalse4035AtCallSite()
    {
        // Story 11 struct-emission audit #5: callee FunctionGenerics must be in scope when the
        // call site resolves/re-validates `array<TKey, TValue>` (KeyIntOrString) on the return type.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function keep_keys<TKey extends int|string, TValue = mixed>(
                array<TKey, TValue> $array
            ): array<TKey, TValue> {
                return $array;
            }

            function demo(array<string, int> $in): array<string, int> {
                return keep_keys($in);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"return-type TKey must not collapse to unresolved at the call site: {Describe(errors)}");
        errors.Should().BeEmpty($"keep_keys call should type-check: {Describe(errors)}");
    }

    [Fact]
    public void ArrayReverse_TyhpdefReturnPreservesKeyParam_NoFalse4035()
    {
        // array_reverse's tyhpdef already returns array<TKey, TValue>; this locks the call-site
        // revalidation path that previously attributed TYHP4035 to the caller's file.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function demo(array<string, int> $in): array<string, int> {
                return \array_reverse($in, true);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"array_reverse return TKey must resolve in callee FunctionGenerics: {Describe(errors)}");
        errors.Should().BeEmpty($"array_reverse call should type-check: {Describe(errors)}");
    }

    [Fact]
    public void GenericFunction_ReturnTypeWithKeyParam_CallerAlsoHasTKey_NoFalse4035()
    {
        // Caller FunctionGenerics named TKey must not steal/shadow resolution of the callee's
        // return annotation — resolve under the callee's generic scope.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function keep_keys<TKey extends int|string, TValue = mixed>(
                array<TKey, TValue> $array
            ): array<TKey, TValue> {
                return $array;
            }

            function demo<TKey extends int|string, TValue = mixed>(
                array<TKey, TValue> $in
            ): array<TKey, TValue> {
                return keep_keys($in);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"caller TKey must not break callee return revalidation: {Describe(errors)}");
        errors.Should().BeEmpty($"nested generic call should type-check: {Describe(errors)}");
    }

    [Fact]
    public void GenericFunction_FirstClassCallable_ReturnTypeWithKeyParam_NoFalse4035()
    {
        // InferCallableSymbol must resolve return annotations in the callee FunctionGenerics
        // scope so `array<TKey, …>` does not collapse to unresolved and fail KeyIntOrString.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function keep_keys<TKey extends int|string, TValue = mixed>(
                array<TKey, TValue> $array
            ): array<TKey, TValue> {
                return $array;
            }

            function demo(): void {
                $fn = keep_keys(...);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"first-class callable must not TYHP4035 on return TKey: {Describe(errors)}");
        errors.Should().BeEmpty($"acquiring first-class keep_keys should type-check: {Describe(errors)}");
    }

    [Fact]
    public void GenericFunction_FirstClassCallable_InvokeWithConcreteArgs_NoFalseMismatch()
    {
        // Invoking a first-class callable acquired from a generic function must bind TKey/TValue
        // from the argument (same as a direct keep_keys($in) call).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function keep_keys<TKey extends int|string, TValue = mixed>(
                array<TKey, TValue> $array
            ): array<TKey, TValue> {
                return $array;
            }

            function demo(): void {
                $fn = keep_keys(...);
                array<string, int> $result = $fn(['a' => 1]);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"invoking first-class keep_keys must not false-positive: {Describe(errors)}");
        errors.Should().BeEmpty($"first-class keep_keys invoke should type-check: {Describe(errors)}");
    }

    [Fact]
    public void GenericFunction_FirstClassCallable_PipeWithConcreteArgs_NoFalseMismatch()
    {
        // `|>` must bind open-generic facet parameters from the LHS the same way `$fn($x)` does.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function keep_keys<TKey extends int|string, TValue = mixed>(
                array<TKey, TValue> $array
            ): array<TKey, TValue> {
                return $array;
            }

            function demo(): void {
                $fn = keep_keys(...);
                array<string, int> $result = ['a' => 1] |> $fn;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"piping into first-class keep_keys must not false-positive: {Describe(errors)}");
        errors.Should().BeEmpty($"first-class keep_keys pipe should type-check: {Describe(errors)}");
    }

    [Fact]
    public void GenericMethod_FirstClassCallable_InvokeWithConcreteArgs_NoFalseMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            class Keys {
                public static function keep<TKey extends int|string, TValue = mixed>(
                    array<TKey, TValue> $array
                ): array<TKey, TValue> {
                    return $array;
                }
            }

            function demo(): void {
                $fn = Keys::keep(...);
                array<string, int> $result = $fn(['a' => 1]);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"invoking first-class Keys::keep must not false-positive: {Describe(errors)}");
        errors.Should().BeEmpty($"first-class method invoke should type-check: {Describe(errors)}");
    }

    [Fact]
    public void GenericFunction_FirstClassCallable_InconsistentTypeArgs_StillReportsError()
    {
        // Binding from the first argument must still reject a second argument that conflicts.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            function same_pair<T>(T $a, T $b): T {
                return $a;
            }

            function demo(): void {
                $fn = same_pair(...);
                $fn(1, 'x');
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            $"inconsistent T args on first-class callable should still error: {Describe(errors)}");
    }

    [Fact]
    public void MethodReturnTypeUtility_ReturnTypeWithClassGenericKeyParam_NoFalse4035()
    {
        // ResolveMethodReturnTypeByName (Story 11 audit #5) must expose the owner's
        // ObjectGenerics when the method's return annotation names a class type parameter,
        // so `array<TKey, TValue>` does not collapse TKey to unresolved and fail KeyIntOrString.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            class Box<TKey extends int|string, TValue> {
                public array<TKey, TValue> $items = [];
                public function toArray(): array<TKey, TValue> {
                    return $this->items;
                }
            }

            function demo(): void {
                __MethodReturnType<Box<string, int>, 'toArray'> $t;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"method return TKey must resolve under owner ObjectGenerics: {Describe(errors)}");
        errors.Should().BeEmpty($"__MethodReturnType on a generic class should type-check: {Describe(errors)}");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    /// <summary>
    /// Compiles and checks a self-contained snippet and returns only the diagnostics that
    /// originate from the snippet file. Compiling against the repo root pulls in the runtime
    /// packages (which currently carry an unrelated, pre-existing bind error), so diagnostics
    /// from other files are filtered out to keep these regression tests focused.
    /// </summary>
    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
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
}
