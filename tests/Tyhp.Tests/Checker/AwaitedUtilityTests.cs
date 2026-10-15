using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>__Awaited&lt;T&gt;</c> stays a deferred wrapper while <c>T</c> is an open type parameter and
/// unwraps <c>\Tyhp\Promise</c> once <c>T</c> is bound (inference, explicit type arguments,
/// expected-type pushdown). Found bugs #451 / #165.
/// </summary>
[Trait("Category", "Checker")]
public class AwaitedUtilityTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class User {
            public function __construct(public string $name) {}
        }

        function findInt(): int {
            return 2;
        }

        function findUser(): User {
            return new User('u');
        }

        async function findUserAsync(): User {
            return new User('a');
        }

        async function findIntAsync(): int {
            return 3;
        }

        final class Runner {
            public static function run<T>(callable(): T $fn): __Awaited<T> {
                throw new \Exception('stub');
            }
        }

        function aw<T>(callable(): T $f): __Awaited<T> {
            throw new \Exception('stub');
        }
        """;

    private static void AssertCompiles(string body)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    private static void AssertRejected(string body, string containing)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().Contain(e => e.Contains(containing),
            "errors were: " + string.Join("; ", result.Errors));
    }

    // ---- plain callables (T bound by inference) ----

    [Fact]
    public void Runner_PlainTypedArrow_IsInt()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Runner::run(fn(): int => 2);
            }
            """);
    }

    [Fact]
    public void Runner_FirstClassCallable_IsInt()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Runner::run(findInt(...));
            }
            """);
    }

    [Fact]
    public void Runner_UntypedArrow_PushesExpectedTypeWithoutPromiseMismatch()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Runner::run(fn() => findInt());
            }
            """);
    }

    [Fact]
    public void Runner_UntypedArrow_InUnwrappedReturnPosition_IsInt()
    {
        AssertCompiles("""
            function t(): int {
                return Runner::run(fn() => findInt());
            }
            """);
    }

    // ---- Promise-returning callables ----

    [Fact]
    public void Runner_PromiseReturningArrow_UnwrapsToInt()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Runner::run(fn(): Promise<int> => findIntAsync());
            }
            """);
    }

    [Fact]
    public void Runner_PromiseReturningArrow_IsNotThePromise()
    {
        AssertRejected("""
            function t(): void {
                Promise<int> $a = Runner::run(fn(): Promise<int> => findIntAsync());
            }
            """, "int");
    }

    [Fact]
    public void Runner_HeldPromiseCallableVariable_UnwrapsToInt()
    {
        AssertCompiles("""
            function t(callable(): Promise<int> $held): void {
                int $a = Runner::run($held);
            }
            """);
    }

    [Fact]
    public void Runner_UntypedArrowReturningPromise_UnwrapsToInt()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Runner::run(fn() => findIntAsync());
            }
            """);
    }

    [Fact]
    public void Runner_AsyncFunctionFirstClassCallable_UnwrapsToInt()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Runner::run(findIntAsync(...));
            }
            """);
    }

    // ---- Promise::run (stub of tyhp/async) ----

    [Fact]
    public void PromiseRun_AsyncArrow_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run(async fn(): User => await findUserAsync());
            }
            """);
    }

    [Fact]
    public void PromiseRun_AsyncClosure_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run(async function (): User { return new User('x'); });
            }
            """);
    }

    [Fact]
    public void PromiseRun_AsyncClosureVariable_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                $c = async fn(): User => await findUserAsync();
                User $u = Promise::run($c);
            }
            """);
    }

    [Fact]
    public void PromiseRun_PlainTypedArrow_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run(fn(): User => findUser());
            }
            """);
    }

    [Fact]
    public void PromiseRun_PlainUntypedArrow_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run(fn() => findUser());
            }
            """);
    }

    [Fact]
    public void PromiseRun_PromiseReturningPlainCallable_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run(fn(): Promise<User> => findUserAsync());
            }
            """);
    }

    [Fact]
    public void PromiseRun_NestedPromiseType_IsRejectedAsPromiseArgument()
    {
        // `Promise<X>` only ever holds an already-awaited `X`.
        AssertRejected("""
            function t(callable(): Promise<Promise<User>> $nested): void {}
            """, "4373");
    }

    [Fact]
    public void PromiseRun_AsyncArrow_IsNotThePromise()
    {
        AssertRejected("""
            function t(): void {
                Promise<User> $u = Promise::run(async fn(): User => await findUserAsync());
            }
            """, "User");
    }

    [Fact]
    public void PromiseRun_ExpectedTypePushdown_DoesNotBindTToPromise()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Promise::run(fn() => findInt());
                User $u = Promise::run(fn() => findUserAsync());
            }
            """);
    }

    // ---- explicit type arguments (#451) ----

    [Fact]
    public void PromiseRun_ExplicitUser_AsyncClosure_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run<User>(async fn(): User => await findUserAsync());
            }
            """);
    }

    [Fact]
    public void PromiseRun_ExplicitUser_PlainClosure_IsUser()
    {
        AssertCompiles("""
            function t(): void {
                User $u = Promise::run<User>(fn(): User => findUser());
            }
            """);
    }

    [Fact]
    public void PromiseRun_ExplicitInt_BothKinds_IsInt()
    {
        AssertCompiles("""
            function t(): void {
                int $a = Promise::run<int>(fn(): int => 2);
                int $b = Promise::run<int>(async fn(): int => await findIntAsync());
            }
            """);
    }

    [Fact]
    public void UserFunction_ExplicitTypeArgument_UnwrapsAwaited()
    {
        AssertCompiles("""
            function t(): void {
                User $a = aw<User>(fn(): User => findUser());
                User $b = aw<User>(async fn(): User => await findUserAsync());
            }
            """);
    }

    [Fact]
    public void UserFunction_InferredPromise_UnwrapsAwaited()
    {
        AssertCompiles("""
            function t(): void {
                User $b = aw(async fn(): User => await findUserAsync());
                User $c = aw(fn(): User => findUser());
            }
            """);
    }

    [Fact]
    public void PromiseRun_ExplicitPromiseTypeArgument_UnwrapsToInner()
    {
        AssertCompiles("""
            function t(callable(): Promise<User> $f): void {
                User $u = Promise::run<Promise<User>>($f);
            }
            """);
    }

    // ---- unions, void, mixed ----

    [Fact]
    public void Awaited_Union_UnwrapsMemberWise()
    {
        AssertCompiles("""
            function t(callable(): Promise<int>|string $f): void {
                int|string $v = Runner::run($f);
            }
            """);
    }

    [Fact]
    public void Awaited_PromiseVoid_IsVoid()
    {
        AssertCompiles("""
            function t(callable(): Promise<void> $f): void {
                Promise::run($f);
            }
            """);
    }

    [Fact]
    public void Awaited_PromiseVoid_NotAssignableToInt()
    {
        AssertRejected("""
            function t(callable(): Promise<void> $f): void {
                int $v = Promise::run($f);
            }
            """, "void");
    }

    [Fact]
    public void Awaited_Mixed_StaysMixed()
    {
        AssertCompiles("""
            function t(callable(): mixed $f): void {
                mixed $v = Runner::run($f);
            }
            """);
    }

    [Fact]
    public void Awaited_NullablePromise_UnwrapsInner()
    {
        AssertCompiles("""
            function t(callable(): ?Promise<int> $f): void {
                ?int $v = Runner::run($f);
            }
            """);
    }

    [Fact]
    public void Awaited_UnboundTypeParameterAtCallSite_BehavesLikeAPlainTypeParameter()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function make<T>(): __Awaited<T> {
                throw new \Exception('x');
            }

            function make2<T>(): T {
                throw new \Exception('x');
            }

            function t(): void {
                mixed $a = make();
                mixed $b = make2();
            }
            """);
        result.Errors.Should().Contain(e => e.Contains("for `make`.", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("for `make2`.", StringComparison.Ordinal));
    }

    // ---- real Promise class only ----

    [Fact]
    public void Awaited_OtherPromiseNamedClass_IsNotUnwrapped()
    {
        AssertCompiles("""
            final class PromiseLike<T> {}

            function t(callable(): PromiseLike<int> $f): void {
                PromiseLike<int> $v = Runner::run($f);
            }
            """);
    }

    [Fact]
    public void Awaited_OtherPromiseNamedClass_IsNotUnwrappedToInner()
    {
        AssertRejected("""
            final class PromiseLike<T> {}

            function t(callable(): PromiseLike<int> $f): void {
                int $v = Runner::run($f);
            }
            """, "int");
    }

    // ---- inside the generic function ----

    [Fact]
    public void Awaited_ReturningOpenTypeParameterInBody_IsRejected()
    {
        // T could be `Promise<X>`, in which case `__Awaited<T>` is `X`.
        AssertRejected("""
            function ident<T>(T $x): __Awaited<T> {
                return $x;
            }
            """, "T");
    }

    [Fact]
    public void Awaited_ReturningMixedInBody_IsRejected()
    {
        // `mixed` is the unknown type, not an escape hatch.
        AssertRejected("""
            function fromMixed<T>(mixed $x): __Awaited<T> {
                return $x;
            }
            """, "mixed");
    }

    [Fact]
    public void Awaited_ReturningCallableResultInBody_IsRejected()
    {
        AssertRejected("""
            function call<T>(callable(): T $f): __Awaited<T> {
                return $f();
            }
            """, "T");
    }

    [Fact]
    public void Plain_ReturningMixedFromTypeParameterReturn_IsRejected()
    {
        // The rule `__Awaited<T>` now follows.
        AssertRejected("""
            function fromMixed<T>(mixed $x): T {
                return $x;
            }
            """, "mixed");
    }

    [Fact]
    public void Awaited_DeferredAwaitedToMixed_Compiles()
    {
        AssertCompiles("""
            function t<T>(__Awaited<T> $a): mixed {
                mixed $m = $a;
                return $a;
            }
            """);
    }

    [Fact]
    public void Awaited_NarrowingMixedWithIs_AllowsTheReturn()
    {
        // The runtime-checked way to hand a settled value back as `__Awaited<T>`.
        AssertCompiles("""
            function settle<T>(callable(): T $f): __Awaited<T> {
                mixed $r = $f();
                if ($r is __Awaited<T>) {
                    return $r;
                }
                throw new \Exception('x');
            }
            """);
    }

    [Fact]
    public void Awaited_ReturningAwaitedOfTheSameParameter_Compiles()
    {
        AssertCompiles("""
            function outer<T>(callable(): T $f): __Awaited<T> {
                return aw($f);
            }
            """);
    }

    [Fact]
    public void Awaited_ReturningNever_Compiles()
    {
        AssertCompiles("""
            function fail(): never {
                throw new \Exception('x');
            }

            function viaNever<T>(callable(): T $f): __Awaited<T> {
                return fail();
            }
            """);
    }

    [Fact]
    public void Runner_UntypedArrow_AfterCallableVariableDeclaration_DoesNotInheritItsReturnType()
    {
        // The annotation of `$cv` contextualizes only its own initializer; it must not leak onto the
        // untyped arrow in the next statement.
        AssertCompiles("""
            function t(): void {
                callable(): User $cv = fn(): User => findUser();
                int $a = Runner::run(fn() => 7);
                echo $a . $cv()->name;
            }
            """);
    }

    [Fact]
    public void Awaited_PromiseLayerOverOpenParameter_NormalizesToDeferredAwaitedOfParameter()
    {
        // `__Awaited<Promise<T>>` is `__Awaited<T>`: the promise layer is removed even though the
        // leaf `T` is still open.
        AssertCompiles("""
            function viaPromise<T extends void|mixed>(callable(): Promise<__Awaited<T>> $f): __Awaited<T> {
                return Runner::run($f);
            }
            """);
    }

    [Fact]
    public void Awaited_PromiseLayerOverOpenParameter_IsNotTheBareParameter()
    {
        AssertRejected("""
            function viaPromise<T extends void|mixed>(callable(): Promise<__Awaited<T>> $f): T {
                return Runner::run($f);
            }
            """, "__Awaited<T>");
    }

    [Fact]
    public void Awaited_UnionWithPromiseLayerOverOpenParameter_NormalizesMemberWise()
    {
        AssertCompiles("""
            function viaUnion<T extends __Awaited<void|mixed>>(callable(): Promise<T>|int $f): __Awaited<T>|int {
                return aw($f);
            }
            """);
    }

    [Fact]
    public void Awaited_MixedToDeferredAwaitedAssignment_IsRejected()
    {
        AssertRejected("""
            function t<T>(mixed $m, callable(): T $f): void {
                __Awaited<T> $a = $m;
            }
            """, "mixed");
    }

    [Fact]
    public void Awaited_OpenTypeParameterToDeferredAwaitedAssignment_IsRejected()
    {
        AssertRejected("""
            function t<T>(T $v): void {
                __Awaited<T> $a = $v;
            }
            """, "T");
    }

    [Fact]
    public void Awaited_DeferredAwaitedToOpenTypeParameter_IsRejected()
    {
        AssertRejected("""
            function t<T>(__Awaited<T> $a): void {
                T $v = $a;
            }
            """, "T");
    }

    [Fact]
    public void Awaited_ConcreteSettledType_UsesOrdinaryAssignability()
    {
        AssertCompiles("""
            function f(): __Awaited<Promise<int>> {
                return 5;
            }
            """);
        AssertRejected("""
            function g(): __Awaited<Promise<int>> {
                return 'x';
            }
            """, "`int`");
    }

    [Fact]
    public void Awaited_MixedToConcreteSettledType_IsRejected()
    {
        AssertRejected("""
            function f(mixed $m): __Awaited<Promise<int>> {
                return $m;
            }
            """, "mixed");
    }

    [Fact]
    public void Awaited_ReturningUnrelatedTypeInBody_IsRejected()
    {
        AssertRejected("""
            function bad<T>(callable(): T $f): __Awaited<T> {
                return 5;
            }
            """, "__Awaited<T>");
    }

    // ---- positions that must not crash ----

    [Fact]
    public void Awaited_InParameterPosition_DoesNotCrash()
    {
        AssertCompiles("""
            function take<T>(__Awaited<T> $value, callable(): T $f): void {}

            function t(): void {
                take(5, fn(): int => 1);
            }
            """);
    }

    [Fact]
    public void Awaited_InsideGenericPromiseArgument_DoesNotCrash()
    {
        AssertCompiles("""
            function wrap<T>(callable(): T $f): Promise<__Awaited<T>> {
                return new Promise<__Awaited<T>>($f);
            }
            """);
    }

    [Fact]
    public void Awaited_InTypeAlias_Resolves()
    {
        AssertCompiles("""
            type Settled = __Awaited<Promise<User>>;

            function t(Settled $u): void {
                echo $u->name;
            }
            """);
    }

    [Fact]
    public void Awaited_AliasOverGenericAlias_Resolves()
    {
        AssertCompiles("""
            type Settled<T> = __Awaited<T>;

            function t(Settled<Promise<User>> $u): void {
                echo $u->name;
            }
            """);
    }
}
