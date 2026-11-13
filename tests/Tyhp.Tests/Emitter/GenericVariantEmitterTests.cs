using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Mechanism D — a callable that needs its own generic parameters at runtime is emitted as one
/// method under the declared name. That method takes the type arguments and returns a
/// <c>\Closure</c> with the value signature.
/// </summary>
[Trait("Category", "Emitter")]
public class GenericVariantEmitterTests
{
    [Fact]
    public void Emit_GenericMethodUsingOwnGeneric_EmitsWrapperAndVariant()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function zero<T>(): mixed {
                    return default(T);
                }
            }
            """);

        php.Should().Contain("function zero(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("#[\\Tyhp\\GenericRuntime(erased: false, binder: \"zero\", layouts: [2]");
        php.Should().Contain("$T = \\Tyhp\\Type::arg($T);");
        php.Should().Contain("return function () use ($T): mixed {");
        php.Should().Contain("return $T->defaultValue();");
        php.Should().NotContain("zero__tyhpGeneric");
    }

    [Fact]
    public void Emit_GenericMethodThatOnlyErasesItsGeneric_EmitsOneSymbol()
    {
        // Nothing reads the bound type at runtime, so the variant would be dead weight.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function identity<T>(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().NotContain("__tyhpGeneric");
        php.Should().NotContain("__generic_");
    }

    [Fact]
    public void Emit_ClosureReturnType_ErasesFreeTypeParameter()
    {
        // FOUND #1b: unbound closure return `T` was emitted as a native PHP type hint and blew up
        // at runtime looking for class `Tyhp\T`. Binding + TypeSpellingHelper must erase it.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public static function resolve<T>(T $value): callable {
                    return static fn(): T => $value;
                }
            }
            """);

        php.Should().Contain("static fn(): mixed => $value");
        php.Should().NotContain("fn(): T");
        php.Should().NotContain(": T =>");
    }

    [Fact]
    public void Emit_ClosureReturnType_ErasedGeneric_ExecutesUnderPhp()
    {
        var output = CompileAndRun("""
            <?tyhp
            namespace Probe;

            class Holder {
                public static function resolve<T>(T $value): callable {
                    return static fn(): T => $value;
                }
            }

            function run(): void {
                $fn = Holder::resolve<int>(42);
                mixed $result = $fn();
                echo (string)$result;
            }
            """);

        output.Should().Be("42");
    }

    [Fact]
    public void Emit_MethodGenericShadowsClassGenericOfSameName_NestedClosureResolvesMethodBinding()
    {
        // A method generic and its enclosing class's generic can share a name (`T`). The nested
        // closure's `typeof(T)` must resolve against the innermost (method) binding captured via
        // Mechanism D, not the class's GenericObject-tracked `T` — otherwise a same-named method
        // generic would silently read the wrong runtime type.
        var output = CompileAndRun("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                private T $classValue;

                public function __construct(T $classValue) {
                    $this->classValue = $classValue;
                }

                public function shadow<T>(T $methodValue): callable {
                    return function () use ($methodValue): string {
                        return '' . typeof(T);
                    };
                }
            }

            function run(): void {
                $box = new Box<string>("class-level");
                $fn = $box->shadow<int>(42);
                echo (string)$fn() . "";
            }
            """);

        output.Should().Be("int");
    }

    [Fact]
    public void Emit_VariantHiddenParameters_HaveNoDefaultValue()
    {
        // Type args are binder-only; the variadic stays on the value Closure — no collision.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function pick<T>(int ...$values): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain("function pick(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("return function (int ...$values) use ($T)");
        php.Should().NotContain("pick__tyhpGeneric");
    }

    [Fact]
    public void Emit_StaticGenericMethod_DelegatesThroughLateStaticBinding()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public static function zero<T>(): mixed {
                    return default(T);
                }
            }
            """);

        php.Should().Contain("static function zero(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().NotContain("zero__tyhpGeneric");
    }

    [Fact]
    public void Emit_GenericFreeFunction_EmitsWrapperAndVariant()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function zero<T>(): mixed {
                return default(T);
            }
            """);

        php.Should().Contain("function zero(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("$T = \\Tyhp\\Type::arg($T);");
        php.Should().NotContain("zero__tyhpGeneric");
    }

    [Fact]
    public void Emit_ByRefParameter_KeepsReferenceThroughTheWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function write<T>(\Tyhp\Type &$out): void {
                    $out = typeof(T);
                }
            }
            """);

        php.Should().Contain("function write(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("return function (\\Tyhp\\Type &$out) use ($T): void");
        php.Should().NotContain("write__tyhpGeneric");
    }

    [Fact]
    public void Emit_ReturnByRef_UsesTemporaryInCleanWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                private mixed $storage = null;

                public function &slot<T>(): mixed {
                    typeof(T);
                    return $this->storage;
                }
            }
            """);

        php.Should().Contain("function slot(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("return function &() use ($T): mixed");
        php.Should().NotContain("slot__tyhpGeneric");
    }

    [Fact]
    public void Emit_NonArrowClosureUsingGeneric_CapturesHiddenParameter()
    {
        // A `function () {}` closure captures nothing implicitly, so without an injected `use` the
        // hidden parameter is undefined inside it.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function later<T>(): callable {
                    return function (): \Tyhp\Type {
                        return typeof(T);
                    };
                }
            }
            """);

        php.Should().Contain("function () use ($T)");
    }

    [Fact]
    public void Emit_NestedClosureUsingGenericAsTypeArgument_CapturesVariantLocal()
    {
        // `new Box<T>()` emits `$T` without typeof/default on the nested closure itself —
        // every binder generic is still captured into nested classic closures.
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Box<TValue> {
                public function __construct(public mixed $value = null): void {}
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }

            class Factory {
                public static function make<T>(): callable {
                    return function () {
                        return new Box<T>();
                    };
                }
            }
            """);

        php.Should().Contain("return function () use ($T): callable {");
        php.Should().Contain("return function () use ($T) {");
        php.Should().Contain("new_Probe_Box__tyhpGeneric($T)");
    }

    [Fact]
    public void Emit_MethodBodyNewOfGenericClass_PassesVariantTypeArgToFactory()
    {
        // PropertyAccessorObject::register pattern: Mechanism D method constructs Mechanism C
        // class with the method's type parameter (not nested inside a classic closure).
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Box<TValue> {
                public function __construct(public mixed $value = null): void {}
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }

            final class Bag {
                public function make<T>(): Box<T> {
                    return new Box<T>();
                }
            }
            """);

        php.Should().Contain("function make(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("new_Probe_Box__tyhpGeneric($T)");
        php.Should().NotContain("new_Probe_Box__tyhpGeneric(null)");
    }

    [Fact]
    public void Emit_NewGeneric_ThroughTyhpdefUseAlias_PreservesTypeArgs()
    {
        // package.tyhpdef `use Tyhp\PropertyAccessor` rewrites the PhpNameAst; grammar addons
        // (type args on `new Box<T>()`) must survive so the factory gets `$__generic_T`, not null.
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            namespace Probe;
            use Probe\Box;
            """,
            """
            <?tyhp
            namespace Probe;

            class Box<TValue> {
                public function __construct(public mixed $value = null): void {}
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }

            final class Bag {
                public function make<T>(): Box<T> {
                    return new Box<T>();
                }
            }
            """);

        php.Should().Contain("new_Probe_Box__tyhpGeneric($T)");
        php.Should().NotContain("new_Probe_Box__tyhpGeneric(null)");
    }

    [Fact]
    public void Emit_ArrowFunctionUsingGeneric_NeedsNoUseClause()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function later<T>(): callable {
                    return fn(): \Tyhp\Type => typeof(T);
                }
            }
            """);

        php.Should().Contain("fn(): \\Tyhp\\Type => $T");
    }

    [Fact]
    public void Emit_SettledVariantLocal_NeedsNoNullCoalesceInsideValueClosure()
    {
        // Unbound type arguments settle inside the value Closure, then body lookups stay bare.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function label<T>(): string {
                    return 'T=' . typeof(T);
                }
            }
            """);

        php.Should().Contain("$T = \\Tyhp\\Type::mixed();");
        php.Should().Contain("'T=' . $T");
        php.Should().NotContain("$T ?? \\");
        php.Should().NotContain("$T?->");
    }

    [Fact]
    public void Emit_TypeofInMatchArm_StillFlagsTheVariant()
    {
        // The rule that visits `typeof` does not reach every expression position, so the flagging pass
        // scans the whole body instead.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function viaMatch<T>(int $n): string {
                    return match ($n) {
                        0 => '' . typeof(T),
                        default => 'other',
                    };
                }
            }
            """);

        php.Should().Contain("function viaMatch(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("@return \\Closure(int $n): string");
        php.Should().Contain("return function (int $n) use ($T)");
    }

    [Fact]
    public void Emit_TypeofInBareStatement_StillFlagsTheVariant()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function nothing<T>(): void {
                    typeof(T);
                }
            }
            """);

        php.Should().Contain("function nothing(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
    }

    [Fact]
    public void Emit_BinderEmitsTypeArgPrologueOutsideValueClosure()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function zero<T>(): mixed {
                    return default(T);
                }
            }
            """);

        php.Should().Contain("$T = \\Tyhp\\Type::arg($T);");
        var prologue = php.IndexOf("$T = \\Tyhp\\Type::arg($T);", StringComparison.Ordinal);
        var returnFn = php.IndexOf("return function", StringComparison.Ordinal);
        prologue.Should().BeGreaterThanOrEqualTo(0);
        returnFn.Should().BeGreaterThan(prologue);
    }

    [Fact]
    public void Emit_NewStaticWithMethodGeneric_EmitsVariantFactoryAndCallableInference()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Box<TValue> {
                public function __construct(private mixed $value): void {}

                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }

                public static function wrap<T>(callable(): T $fn): self<T> {
                    return new self<T>($fn());
                }
            }
            """);

        php.Should().Contain("function wrap(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("\\Tyhp\\Type::fromCallableReturn($fn)");
        php.Should().Contain("return function (callable $fn) use ($T)");
        php.Should().Contain("new_Probe_Box__tyhpGeneric");
        php.Should().NotContain("wrap__tyhpGeneric");
        var prologue = php.IndexOf("$T = \\Tyhp\\Type::arg($T);", StringComparison.Ordinal);
        var returnFn = php.IndexOf(
            "return function (callable $fn) use ($T)",
            StringComparison.Ordinal);
        prologue.Should().BeGreaterThanOrEqualTo(0);
        returnFn.Should().BeGreaterThan(prologue);
    }

    private const string AsyncCallableReturnHost = """
        <?tyhp
        namespace Probe;

        function pick<T>(callable(): T $fn): mixed {
            return typeof(T);
        }

        function pair<A, B>(callable(): A $first, callable(): B $second): mixed {
            return typeof(A);
        }

        class Runner {
            public static function make<T>(callable(): T $fn): mixed {
                return typeof(T);
            }
        }

        """;

    [Theory]
    [InlineData(
        "return pick(async function(): int { return 1; });",
        "pick(\\Tyhp\\Type::int())(")]
    [InlineData(
        "return pick(async fn(): string => 'x');",
        "pick(\\Tyhp\\Type::string())(")]
    [InlineData(
        "return pick(fn: async function(): int { return 1; });",
        "pick(\\Tyhp\\Type::int())(")]
    [InlineData(
        "return Runner::make(async function(): bool { return true; });",
        "Runner::make(\\Tyhp\\Type::bool())(")]
    [InlineData(
        "return pair(async function(): int { return 1; }, fn(): string => 'x');",
        "pair(\\Tyhp\\Type::int(), \\Tyhp\\Type::string())(")]
    public void Emit_AsyncClosureArgument_BindsAwaitedTypeAtCallSite(string statement, string expected)
    {
        var php = CompileAndEmit(AsyncCallableReturnHost + $$"""
            function run(): mixed {
                {{statement}}
            }
            """);

        php.Should().Contain(expected);
    }

    [Fact]
    public void Emit_AsyncClosureWithoutDeclaredReturn_LeavesTypeArgumentToBinderDefault()
    {
        var php = CompileAndEmit(AsyncCallableReturnHost + """
            function run(): mixed {
                return pick(async function() { return 1; });
            }
            """);

        php.Should().Contain("pick(null)(");
        php.Should().NotContain("fromCallableReturn(function");
    }

    [Fact]
    public void Emit_AsyncClosureVariable_BindsAwaitedTypeAtCallSite()
    {
        var php = CompileAndEmit(AsyncCallableReturnHost + """
            function run(): mixed {
                $fn = async function(): int { return 1; };
                return pick($fn);
            }
            """);

        php.Should().Contain("pick(\\Tyhp\\Type::int())(");
        php.Should().NotContain("return pick($fn)");
    }

    [Theory]
    [InlineData("return pick(function(): int { return 1; });")]
    [InlineData("callable(): int $f = fn(): int => 1; return pick($f);")]
    public void Emit_NonAsyncCallableArgument_KeepsDeclaredNameWrapper(string statement)
    {
        var php = CompileAndEmit(AsyncCallableReturnHost + $$"""
            function run(): mixed {
                {{statement}}
            }
            """);

        php.Should().Contain("pick(");
        php.Should().NotContain("pick__tyhpGeneric(\\Tyhp\\Type::int())(");
    }

    [Fact]
    public void Emit_AsyncClosureAlongsideNonLiteralCallable_KeepsDeclaredNameWrapper()
    {
        var php = CompileAndEmit(AsyncCallableReturnHost + """
            function run(callable(): string $second): mixed {
                return pair(async function(): int { return 1; }, $second);
            }
            """);

        php.Should().Contain("return pair(");
        php.Should().NotContain("pair__tyhpGeneric(\\Tyhp\\Type::int()");
    }

    [Theory]
    [InlineData("return Holder::zero<int>();", "Holder::zero(\\Tyhp\\Type::int())()")]
    [InlineData("return $h->zero<int>();", "$h->zero(\\Tyhp\\Type::int())()")]
    [InlineData("return free<int>();", "free(\\Tyhp\\Type::int())()")]
    [InlineData("$h->zero<int>();", "$h->zero(\\Tyhp\\Type::int())()")]
    [InlineData("return [$h->zero<int>()];", "[$h->zero(\\Tyhp\\Type::int())()]")]
    [InlineData("return sink($h->zero<int>());", "sink($h->zero(\\Tyhp\\Type::int())())")]
    [InlineData(
        "return sink(fn(): mixed => $h->zero<int>());",
        "$h->zero(\\Tyhp\\Type::int())()")]
    public void Emit_CallSiteWithTypeArguments_RoutesToVariant(string callSite, string expected)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            class Holder {
                public static function zero<T>(): mixed {
                    return default(T);
                }
            }

            function free<T>(): mixed {
                return default(T);
            }

            function sink(mixed $v): mixed {
                return $v;
            }

            function caller(Holder $h): mixed {
                {{callSite}}
                return null;
            }
            """);

        php.Should().Contain(expected);
    }

    [Fact]
    public void Emit_CallSiteWithoutTypeArguments_PassesNullToTheBinder()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function zero<T>(): mixed {
                    return default(T);
                }
            }

            function caller(Holder $h): mixed {
                return $h->zero();
            }
            """);

        php.Should().Contain("return $h->zero(null)();");
    }

    [Fact]
    public void Emit_MultipleTypeParameters_PassesThemInDeclarationOrder()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function pair<TA, TB>(): string {
                    return typeof(TA) . '|' . typeof(TB);
                }
            }

            function caller(Holder $h): string {
                return $h->pair<int, string>();
            }
            """);

        php.Should().Contain(
            "function pair(\\Tyhp\\TypeLike|string|null $TA = null, \\Tyhp\\TypeLike|string|null $TB = null): \\Closure");
        php.Should().Contain(
            "$h->pair(\\Tyhp\\Type::int(), \\Tyhp\\Type::string())()");
    }

    /// <summary>
    /// An interface has no body to infer the requirement from, so the flag has to travel up from the
    /// implementation. Both signatures belong to the contract: a call typed as the interface targets
    /// the binder, so every implementation must be required to declare it.
    /// </summary>
    [Fact]
    public void Emit_InterfaceDeclaringAGenericMethod_DeclaresBothSignatures()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            interface Named {
                public function name<T>(): string;
            }

            class Holder implements Named {
                public function name<T>(): string {
                    return '' . typeof(T);
                }
            }
            """);

        php.Should().Contain("public function name(\\Tyhp\\TypeLike|string|null $T = null): \\Closure;");
        php.Should().NotContain("function name__tyhpGeneric");
        php.Should().Contain("@return \\Closure(): string");
    }

    [Fact]
    public void Emit_BinderDocComment_IncludesNamedClosureParameters()
    {
        // PhpStorm/Psalm need `$name` (and `=` for optionals) in `@return \Closure(...)` to accept
        // named arguments at call sites of the returned binder Closure.
        var php = CompileAndEmit("""
            <?tyhp
            class Factory {
                public static function settled<T>(
                    string $state,
                    mixed $value = null,
                    ?\Throwable $error = null,
                ): mixed {
                    return $value ?? default(T);
                }
            }
            """);

        php.Should().Contain(
            "@return \\Closure(string $state, mixed $value=, ?\\Throwable $error=): mixed");
    }

    [Fact]
    public void Emit_AbstractGenericMethod_DeclaresBothSignaturesWithoutABody()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            abstract class Base {
                public abstract function name<T>(): string;
            }

            class Holder extends Base {
                public function name<T>(): string {
                    return '' . typeof(T);
                }
            }
            """);

        php.Should().Contain(
            "abstract public function name(\\Tyhp\\TypeLike|string|null $T = null): \\Closure;");
        php.Should().NotContain("function name__tyhpGeneric");
        php.Should().NotContain(
            "abstract public function name(\\Tyhp\\TypeLike|string|null $T = null): \\Closure\n{");
    }

    /// <summary>
    /// An <c>async</c> callable's outward-facing form returns a promise rather than its declared type,
    /// so the wrapper has to be built by the same rule as the non-generic path — otherwise it declares
    /// the inner type and PHP rejects the promise the Closure hands back. The binder still returns
    /// <c>\Closure</c>; the Closure returns the promise.
    /// </summary>
    [Fact]
    public void Emit_AsyncGenericMethod_KeepsThePromiseReturnOnBothHalves()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Holder {
                public async function name<T>(): string {
                    return '' . typeof(T);
                }
            }
            """);

        php.Should().Contain(
            "public function name(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("return function () use ($T): \\Tyhp\\Promise");
        php.Should().NotContain("name__tyhpGeneric");
    }

    /// <summary>
    /// An erased async body still has to stamp the settled type parameter onto the Promise.
    /// The inner closure's PHP return hint is <c>mixed</c>, so <c>_async</c> would infer
    /// <c>Promise&lt;mixed&gt;</c>. The variant passes the bound return type instead.
    /// </summary>
    [Fact]
    public void Emit_InferGenericAsyncMethod_StampsSettledTypeOnThePromise()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Holder {
                #[\Tyhp\InferGeneric]
                public async function pick<T>(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().Contain("function pick__tyhpGeneric");
        php.Should().Contain("\\Tyhp\\Promise::_async__tyhpGeneric($T)(function ()");
    }

    [Fact]
    public void Emit_AsyncGenericMethodWithoutInferGeneric_StaysPlainWhenTheBodyDoesNotReify()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Holder {
                public async function pick<T>(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().NotContain("pick__tyhpGeneric");
        php.Should().Contain("\\Tyhp\\Promise::_async(function ()");
        php.Should().NotContain("_async__tyhpGeneric");
    }

    /// <summary>
    /// A call site may pass the enclosing class's generic as the type argument to a method generic —
    /// `Type::isType&lt;TValue&gt;($result)` in `PropertyAccessor` is the runtime package's real use of
    /// this. The argument has to be the instance lookup for the class generic, since that is where the
    /// bound type lives, rather than an erased `mixed`.
    /// </summary>
    [Fact]
    public void Emit_CallSitePassingAClassGenericAsTypeArgument_ForwardsTheInstanceLookup()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Checker {
                public static function isType<TTarget>(mixed $value): string {
                    return '' . typeof(TTarget);
                }
            }

            class Accessor<TValue> {
                public function check(mixed $result): string {
                    return Checker::isType<TValue>($result);
                }
            }
            """);

        php.Should().Contain(
            "Checker::isType($this->__tyhpGeneric->resolvedType(\\Accessor::class, 'TValue'))($result)");
    }

    [Fact]
    public void Emit_CallSitePassingOwnMethodGeneric_ForwardsTheBinderCapture()
    {
        // Callee is Mechanism D; caller mentions T only as a type argument. That still needs the
        // bound type at runtime (to pass into the callee's binder), so the caller is a variant too.
        var php = CompileAndEmit("""
            <?tyhp
            class Helper {
                public static function take<T>(mixed $value): mixed {
                    return typeof(T);
                }
            }

            class Holder {
                public static function wrap<T>(T $value): mixed {
                    return Helper::take<T>($value);
                }
            }
            """);

        php.Should().Contain("function wrap(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("Helper::take($T)($value)");
        php.Should().NotContain("fromClassName(\\T::class)");
        php.Should().NotContain("fromClassName(T::class)");
        php.Should().NotContain("Helper::take(\\Tyhp\\Type::mixed())");
    }

    [Fact]
    public void Emit_TypeParameterClassConstant_ReadsTheBoundClassName()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function id<T>(): string {
                return T::class;
            }
            """);

        php.Should().Contain("\\ltrim($T->getName() ?? '', '\\\\')");
        php.Should().NotContain("T::class");
    }

    [Fact]
    public void Emit_ExtensionMethodUsingOwnGeneric_EmitsVariant()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringJson extends string {
                function decodeAs<T>(): mixed {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain("function decodeAs(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("binder: \"decodeAs\", layouts: [2]");
    }

    [Trait("Category", "PHP")]
    [Fact]
    public void Emit_MechanismD_ResolvesBoundTypesWhenExecuted()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var output = CompileAndRun("""
            <?tyhp
            namespace Probe;

            class Holder {
                public static function name<T>(): string {
                    return '' . typeof(T);
                }

                public function zero<T>(): mixed {
                    return default(T);
                }

                public function pair<TA, TB>(): string {
                    return typeof(TA) . '|' . typeof(TB);
                }

                public function later<T>(): callable {
                    return function (): string {
                        return '' . typeof(T);
                    };
                }

                public function write<T>(\Tyhp\Type &$out): void {
                    $out = typeof(T);
                }
            }

            function firstOr<T>(array $items): mixed {
                foreach ($items as $item) {
                    return $item;
                }

                return default(T);
            }

            function run(): void {
                $h = new Holder();

                echo Holder::name<int>() . "\n";
                echo Holder::name() . "\n";

                var_dump($h->zero<int>());
                var_dump($h->zero<string>());
                var_dump($h->zero<bool>());
                var_dump($h->zero());

                echo $h->pair<int, string>() . "\n";

                $later = $h->later<float>();
                echo (string)$later() . "\n";

                var_dump(firstOr<int>([]));
                var_dump(firstOr<int>([7]));
            }
            """);

        output.Should().Be(
            """
            int
            mixed
            int(0)
            string(0) ""
            bool(false)
            NULL
            int|string
            float
            int(0)
            int(7)

            """.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A call site binds against the statically known method, so dispatching through a contract or a
    /// base class has to reach the implementation's binder with the type argument intact.
    /// </summary>
    [Trait("Category", "PHP")]
    [Fact]
    public void Emit_MechanismD_ResolvesBoundTypesThroughAHierarchy()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var output = CompileAndRun("""
            <?tyhp
            namespace Probe;

            interface Named {
                public function name<T>(): string;
            }

            abstract class Base implements Named {
                public abstract function zero<T>(): mixed;
            }

            class Holder extends Base {
                public function name<T>(): string {
                    return 'holder:' . typeof(T);
                }

                public function zero<T>(): mixed {
                    return default(T);
                }
            }

            class Other extends Base {
                public function name<T>(): string {
                    return 'other:' . typeof(T);
                }

                // Never reads T, but a call through the contract still targets the binder.
                public function zero<T>(): mixed {
                    return 'fixed';
                }
            }

            function describe(Named $named): string {
                return $named->name<int>();
            }

            function zeroOf(Base $base): mixed {
                return $base->zero<string>();
            }

            function run(): void {
                echo describe(new Holder()) . "\n";
                echo describe(new Other()) . "\n";
                var_dump(zeroOf(new Holder()));
                var_dump(zeroOf(new Other()));
            }
            """);

        output.Should().Be(
            """
            holder:int
            other:int
            string(0) ""
            string(5) "fixed"

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Emit_TypeParameterDefault_SettlesInsideTheClosure()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function make<T = string>(): mixed {
                return default(T);
            }
            """);

        php.Should().Contain("$T = \\Tyhp\\Type::string();");
        php.Should().NotContain("$T = \\Tyhp\\Type::mixed();");
    }

    [Fact]
    public void Emit_ClosureInfersClassNameAndValueAndUnion()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function fromClass<T>(__ClassName<T> $class): mixed {
                return typeof(T);
            }

            function fromValue<T>(?T $value): mixed {
                return typeof(T);
            }

            function fromPair<T>(T $left, T $right): mixed {
                return typeof(T);
            }

            function fromLabel<T = int>(string $label): mixed {
                return default(T);
            }

            function unbound<T>(string $label): mixed {
                return typeof(T);
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::fromClassName($class)");
        php.Should().Contain("if ($value !== null)");
        php.Should().Contain("\\Tyhp\\Type::of($value)");
        php.Should().Contain("\\Tyhp\\Type::of($left)");
        php.Should().Contain("\\Tyhp\\Type::of($right)");
        php.Should().Contain("->or(");
        php.Should().Contain("$T = \\Tyhp\\Type::int();");
        php.Should().Contain("$T = \\Tyhp\\Type::mixed();");
    }

    [Theory]
    [InlineData("$f = free(...);", "$f = free(null);")]
    [InlineData("$f = free<int>(...);", "$f = free(\\Tyhp\\Type::int());")]
    [InlineData("$f = Holder::zero<int>(...);", "$f = Holder::zero(\\Tyhp\\Type::int());")]
    [InlineData("$f = $h->name<int>(...);", "$f = $h->name(\\Tyhp\\Type::int());")]
    [InlineData("$f = $h->name(...);", "$f = $h->name(null);")]
    public void Emit_FirstClassCallableOfBinderFirstGeneric_IsTheBinderResult(string statement, string expected)
    {
        // The binder returns the Closure with the value signature, which is what `f(...)` denotes.
        // Emitting `free(...)` would hand back the binder itself.
        var php = CompileAndEmit($$"""
            <?tyhp
            class Holder {
                public static function zero<T>(): mixed {
                    return default(T);
                }

                public function name<T>(): string {
                    return '' . typeof(T);
                }
            }

            function free<T>(): mixed {
                return default(T);
            }

            function caller(Holder $h): mixed {
                {{statement}}
                return $f();
            }
            """);

        php.Should().Contain(expected);
        php.Should().NotContain("(...)");
    }

    [Fact]
    public void Emit_PipeIntoFirstClassCallableOfBinderFirstGeneric_AppliesTheValueToTheBinderResult()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function label<T>(T $value): string {
                return '' . typeof(T);
            }

            function caller(int $n): string {
                return $n |> label<int>(...);
            }
            """);

        php.Should().Contain("label(\\Tyhp\\Type::int())($n)");
    }

    [Fact]
    public void Emit_VariadicValueParameter_InfersFromEachArgument()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function firstOf<T>(T ...$items): mixed {
                return typeof(T);
            }
            """);

        php.Should().Contain("foreach ($items as $__tyhpItem_T_0)");
        php.Should().Contain("if ($__tyhpItem_T_0 !== null)");
        php.Should().Contain("\\Tyhp\\Type::of($__tyhpItem_T_0)");
        php.Should().NotContain("\\Tyhp\\Type::of($items)");
    }

    [Fact]
    public void Emit_NullableClassNameAndCallableParameters_GuardNullBeforeInferring()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function fromClass<T>(?__ClassName<T> $class): mixed {
                return typeof(T);
            }

            function fromCallable<T>(?callable(): T $fn): mixed {
                return typeof(T);
            }
            """);

        php.Should().Contain("if ($class !== null)");
        php.Should().Contain("if ($fn !== null)");
        php.Should().Contain("\\Tyhp\\Type::fromClassName($class)");
        php.Should().Contain("\\Tyhp\\Type::fromCallableReturn($fn)");
    }

    [Fact]
    public void Emit_BinderDocComment_StartsWithTheTwoStepCallThenTheAuthorDocThenTheTags()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                /**
                 * Describes the bound type.
                 */
                public static function describe<T>(string $label, int ...$rest): string {
                    return $label . typeof(T);
                }
            }
            """);

        var call = php.IndexOf("Holder::describe($T)($label, ...$rest)", StringComparison.Ordinal);
        var author = php.IndexOf("Describes the bound type.", StringComparison.Ordinal);
        var template = php.IndexOf("@template T", StringComparison.Ordinal);
        var param = php.IndexOf(
            "@param \\Tyhp\\Type<T>|\\Tyhp\\BuiltIn|class-string<T>|null $T",
            StringComparison.Ordinal);
        var returns = php.IndexOf(
            "@return \\Closure(string $label, int ...$rest): string",
            StringComparison.Ordinal);

        call.Should().BeGreaterThanOrEqualTo(0);
        author.Should().BeGreaterThan(call);
        template.Should().BeGreaterThan(author);
        param.Should().BeGreaterThan(template);
        returns.Should().BeGreaterThan(param);
    }

    /// <summary>
    /// Inference runs inside the Closure on every call, and a first-class callable of the binder
    /// is the value Closure.
    /// </summary>
    [Trait("Category", "PHP")]
    [Fact]
    public void Emit_MechanismD_InfersTypeArgumentsPerCallWhenExecuted()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var output = CompileAndRun("""
            <?tyhp
            namespace Probe;

            function pick<T>(T ...$items): string {
                return '' . typeof(T);
            }

            function viaClass<T>(__ClassName<T> $class): string {
                return '' . typeof(T);
            }

            function maybe<T>(?callable(): T $fn = null): string {
                return '' . typeof(T);
            }

            function run(): void {
                echo pick(1, 2) . "\n";
                echo pick() . "\n";
                echo viaClass(\ArrayObject::class) . "\n";
                echo maybe() . "\n";
                echo pick<float>(1.5) . "\n";
                $each = pick<bool>(...);
                echo $each(true) . "\n";
                echo pick(1) . "|" . pick('a') . "\n";
            }
            """);

        output.Should().Be(
            """
            int
            mixed
            ArrayObject
            mixed
            float
            bool
            int|string

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Emit_Layout2ForeignCallable_CallsTheDeclaredNameWithAndWithoutTypeArguments()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                #[\Tyhp\GenericRuntime(erased: false, binder: "zero", layouts: [2])]
                public function zero<T>(): mixed;
            }
            """,
            """
            <?tyhp
            function run(Holder $h): mixed {
                $a = $h->zero<int>();
                $f = $h->zero<int>(...);
                return $h->zero();
            }
            """);

        php.Should().Contain("$a = $h->zero(\\Tyhp\\Type::int())();");
        php.Should().Contain("$f = $h->zero(\\Tyhp\\Type::int());");
        php.Should().Contain("return $h->zero(null)();");
        php.Should().NotContain("__tyhpGeneric");
        php.Should().NotContain("\\Tyhp\\Generic::bind");
    }

    [Fact]
    public void Emit_Layout1ForeignCallable_FirstClassCallableKeepsTheWrapper()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                #[\Tyhp\GenericRuntime(erased: false, binder: "zero__tyhpGeneric", layouts: [1])]
                public function zero<T>(): mixed;
            }
            """,
            """
            <?tyhp
            function run(Holder $h): mixed {
                $f = $h->zero(...);
                return $f();
            }
            """);

        php.Should().Contain("$f = $h->zero(...);");
    }

    [Fact]
    public void Emit_Layout1ForeignCallable_FirstClassCallableWithTypeArgumentsBindsThem()
    {
        // The layout-1 binder returns the value Closure, which is what `zero<int>(...)` denotes.
        // A bare `$h->zero(...)` would drop `<int>`.
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                #[\Tyhp\GenericRuntime(erased: false, binder: "zero__tyhpGeneric", layouts: [1])]
                public function zero<T>(): mixed;

                #[\Tyhp\GenericRuntime(erased: false, binder: "make__tyhpGeneric", layouts: [1])]
                public static function make<T>(T $value): mixed;
            }
            """,
            """
            <?tyhp
            function run(Holder $h): mixed {
                $f = $h->zero<int>(...);
                $g = Holder::make<string>(...);
                return $f();
            }
            """);

        php.Should().Contain("$f = $h->zero__tyhpGeneric(\\Tyhp\\Type::int());");
        php.Should().Contain("$g = Holder::make__tyhpGeneric(\\Tyhp\\Type::string());");
        php.Should().NotContain("(...)");
    }

    [Fact]
    public void Emit_PipeIntoLayout1ForeignFirstClassCallable_AppliesTheValueToTheBoundClosure()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                #[\Tyhp\GenericRuntime(erased: false, binder: "label__tyhpGeneric", layouts: [1])]
                public static function label<T>(T $value): string;
            }
            """,
            """
            <?tyhp
            function run(int $n): string {
                return $n |> Holder::label<int>(...);
            }
            """);

        php.Should().Contain("Holder::label__tyhpGeneric(\\Tyhp\\Type::int())($n)");
    }

    [Fact]
    public void Emit_Layout1ForeignCallable_UsesTheStampedBinder()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                #[\Tyhp\GenericRuntime(erased: false, binder: "zero__tyhpGeneric", layouts: [1])]
                public function zero<T>(): mixed;
            }
            """,
            """
            <?tyhp
            function run(Holder $h): mixed {
                return $h->zero<int>();
            }
            """);

        php.Should().Contain("$h->zero__tyhpGeneric(\\Tyhp\\Type::int())()");
        php.Should().NotContain("\\Tyhp\\Generic::bind");
    }

    [Fact]
    public void Emit_InferGeneric_EmitsValueSignatureAndLayout1Companion()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                #[\Tyhp\InferGeneric]
                public function zero<T>(T $value): mixed {
                    return default(T);
                }
            }
            """);

        php.Should().Contain("function zero(mixed $value): mixed");
        php.Should().Contain("function zero__tyhpGeneric(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("#[\\Tyhp\\GenericRuntime(erased: false, binder: \"zero__tyhpGeneric\", layouts: [1]");
        php.Should().NotContain("layouts: [2]");
        php.Should().Contain("return $this->zero__tyhpGeneric(null)($value);");
    }

    [Fact]
    public void Emit_ErasedInferGeneric_StaysPlain()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\InferGeneric]
            function identity<T>(T $value): T {
                return $value;
            }
            """);

        php.Should().NotContain("identity__tyhpGeneric");
        php.Should().NotContain("layouts: [2]");
    }

    [Fact]
    public void Emit_InferGenericCall_BareUsesPlain_ExplicitUsesCompanion()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\InferGeneric]
            function label<T>(T $value): string {
                return '' . typeof(T);
            }

            function run(): void {
                label(1);
                label<int>(1);
            }
            """);

        php.Should().Contain("label(1);");
        php.Should().Contain("label__tyhpGeneric(\\Tyhp\\Type::int())(1);");
    }

    [Fact]
    public void Run_InferGeneric_BindsCompanionAndInfersPromiseArrays()
    {
        var files = Compile("""
            <?tyhp
            namespace Probe;

            class Promise<T> {
                public function __construct(public T $value) {}
            }

            #[\Tyhp\InferGeneric]
            function take<T = string>(?Promise<T> $promise): string {
                return '' . typeof(T);
            }

            #[\Tyhp\InferGeneric]
            function all<T = string>(array<Promise<T>> $promises): string {
                return '' . typeof(T);
            }

            #[\Tyhp\InferGeneric]
            function value<T = string>(?T $value): string {
                return '' . typeof(T);
            }

            #[\Tyhp\InferGeneric]
            function spread<T = string>(Promise<T> ...$promises): string {
                return '' . typeof(T);
            }
            """).Files;

        var output = EmittedPhpRunner.RunAgainstRuntimeDist(files, """
            $intPromise = \Probe\Promise::new(\Tyhp\Type::int)(1);
            $stringPromise = \Probe\Promise::new(\Tyhp\Type::string)('a');
            echo \Probe\all([$intPromise, $stringPromise]), "\n";
            echo \Probe\all([]), "\n";
            echo \Probe\take(null), "\n";
            echo \Probe\value(null), "\n";
            $bound = \Tyhp\Generic::bind('Probe\\take__tyhpGeneric', \Tyhp\Type::int);
            echo $bound(null), "\n";
            echo \Probe\spread($intPromise, $stringPromise), "\n";
            echo \Probe\spread(), "\n";
            """, "core");

        output.Should().NotBeNull();
        output!.TrimEnd().Should().Be("""
            int|string
            string
            string
            string
            int
            int|string
            string
            """.TrimEnd());
    }

    [Fact]
    public void Emit_InferGeneric_ValueOrPromiseUnion_ReadsEachArmOnce()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Promise<T> {
                public function __construct(public T $value) {}
            }

            #[\Tyhp\InferGeneric]
            function either<T = string>(null|T|Promise<T> $value = null): string {
                return '' . typeof(T);
            }
            """);

        php.Should().Contain("if ($value instanceof \\Probe\\Promise) {");
        php.Should().Contain("if ($value !== null && !($value instanceof \\Probe\\Promise)) {");
        php.Should().Contain("\\Tyhp\\Type::of($value)->typeArgument(0)");
    }

    [Fact]
    public void Run_InferGeneric_InfersFromValueOrPromiseUnion()
    {
        var files = Compile("""
            <?tyhp
            namespace Probe;

            class Promise<T> {
                public function __construct(public T $value) {}
            }

            #[\Tyhp\InferGeneric]
            function either<T = string>(null|T|Promise<T> $value = null): string {
                return '' . typeof(T);
            }

            #[\Tyhp\InferGeneric]
            function eitherMany<T = string>(null|T|Promise<T> ...$values): string {
                return '' . typeof(T);
            }
            """).Files;

        var output = EmittedPhpRunner.RunAgainstRuntimeDist(files, """
            $intPromise = \Probe\Promise::new(\Tyhp\Type::int)(1);
            echo \Probe\either(5), "\n";
            echo \Probe\either($intPromise), "\n";
            echo \Probe\either('x'), "\n";
            echo \Probe\either(null), "\n";
            echo \Probe\either(), "\n";
            echo \Probe\eitherMany(5, $intPromise, 'x'), "\n";
            """, "core");

        output.Should().NotBeNull();
        output!.TrimEnd().Should().Be("""
            int
            int
            string
            string
            string
            int|string
            """.TrimEnd());
    }

    private static string CompileAndEmit(string tyhp)
    {
        var (php, _) = Compile(tyhp);
        return php;
    }

    private static string CompileAndEmitWithTyhpdef(string tyhpdef, string tyhp)
    {
        var (php, _) = Compile(tyhp, tyhpdef);
        return php;
    }

    /// <summary>
    /// Emits <paramref name="tyhp"/>, writes it next to a driver that autoloads the
    /// test PHP runtime fixture, and returns what PHP printed.
    /// </summary>
    private static string CompileAndRun(string tyhp) =>
        EmittedPhpRunner.Run(Compile(tyhp).Files, "\\Probe\\run();");

    private static (string Php, IReadOnlyList<PHPOutputFile> Files) Compile(string tyhp, string? tyhpdef = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "generic-variant.tyhp");
        File.WriteAllText(filePath, tyhp);
        string? tyhpdefPath = null;
        if (tyhpdef is not null)
        {
            tyhpdefPath = Path.Combine(tempDir, "aliases.tyhpdef");
            File.WriteAllText(tyhpdefPath, tyhpdef);
        }

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var files = tyhpdefPath is null
                ? new[] { filePath }
                : new[] { tyhpdefPath, filePath };
            var result = compilationService.ParseFiles(files, IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return (
                string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)),
                outputFiles);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
