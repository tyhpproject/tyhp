using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Mechanism D — a callable that needs its own generic parameters at runtime is emitted as a pair:
/// the declared name keeps the declared signature and delegates with null type args, and a
/// <c>__tyhpGeneric</c> binder returns a <c>\Closure</c> with the declared value signature.
/// See FOUND_BUGS Mechanism D.
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

        php.Should().Contain("function zero(): mixed");
        php.Should().Contain("return $this->zero__tyhpGeneric(null)();");
        php.Should().Contain("function zero__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("return function () use ($__generic_T): mixed {");
        php.Should().Contain("return $__generic_T->defaultValue();");
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

        php.Should().Contain("function pick__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("return function (int ...$values) use ($__generic_T)");
        php.Should().NotContain("$__generic_T = null");
        php.Should().Contain("return $this->pick__tyhpGeneric(null)(...$values);");
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

        php.Should().Contain("return static::zero__tyhpGeneric(null)();");
        php.Should().Contain("static function zero__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
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

        php.Should().Contain("function zero(): mixed");
        php.Should().Contain("return zero__tyhpGeneric(null)();");
        php.Should().Contain("function zero__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
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

        php.Should().Contain("function write__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("return function (\\Tyhp\\Type &$out) use ($__generic_T): void");
        php.Should().Contain("$this->write__tyhpGeneric(null)($out);");
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

        php.Should().Contain("function &slot(): mixed");
        php.Should().Contain("$fn = $this->slot__tyhpGeneric(null);");
        php.Should().Contain("return $fn();");
        php.Should().Contain("function slot__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("return function &() use ($__generic_T): mixed");
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

        php.Should().Contain("function () use ($__generic_T)");
    }

    [Fact]
    public void Emit_NestedClosureUsingGenericAsTypeArgument_CapturesVariantLocal()
    {
        // `new Box<T>()` emits `$__generic_T` without typeof/default on the closure itself —
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

        php.Should().Contain("return function () use ($__generic_T): callable {");
        php.Should().Contain("return function () use ($__generic_T) {");
        php.Should().Contain("new_Probe_Box__tyhpGeneric($__generic_T)");
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

        php.Should().Contain("function make__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("new_Probe_Box__tyhpGeneric($__generic_T)");
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

        php.Should().Contain("new_Probe_Box__tyhpGeneric($__generic_T)");
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

        php.Should().Contain("fn(): \\Tyhp\\Type => $__generic_T");
    }

    [Fact]
    public void Emit_SettledVariantLocal_NeedsNoNullCoalesceInsideValueClosure()
    {
        // Prologue `$__generic_T ??= …` runs before `return function`, so body lookups stay bare.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function label<T>(): string {
                    return 'T=' . typeof(T);
                }
            }
            """);

        php.Should().Contain("$__generic_T ??= \\Tyhp\\Type::mixed();");
        php.Should().Contain("'T=' . $__generic_T");
        php.Should().NotContain("$__generic_T ?? \\");
        php.Should().NotContain("$__generic_T?->");
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

        php.Should().Contain("function viaMatch__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("@return \\Closure(int $n): string");
        php.Should().Contain("return function (int $n) use ($__generic_T)");
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

        php.Should().Contain("function nothing__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
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

        php.Should().Contain("$__generic_T ??= \\Tyhp\\Type::mixed();");
        var prologue = php.IndexOf("$__generic_T ??= \\Tyhp\\Type::mixed();", StringComparison.Ordinal);
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

                public static function wrap<T>(callable<T> $fn): self<T> {
                    return new self<T>($fn());
                }
            }
            """);

        php.Should().Contain("function wrap(callable $fn): self");
        php.Should().Contain(
            "return static::wrap__tyhpGeneric(\\Tyhp\\Type::fromCallableReturn($fn))($fn);");
        php.Should().Contain("function wrap__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("$__generic_T ??= \\Tyhp\\Type::mixed();");
        php.Should().Contain("new_Probe_Box__tyhpGeneric");
        var prologue = php.IndexOf("$__generic_T ??= \\Tyhp\\Type::mixed();", StringComparison.Ordinal);
        var returnFn = php.IndexOf(
            "return function (callable $fn) use ($__generic_T): self",
            StringComparison.Ordinal);
        prologue.Should().BeGreaterThanOrEqualTo(0);
        returnFn.Should().BeGreaterThan(prologue);
    }

    [Theory]
    [InlineData("return Holder::zero<int>();", "Holder::zero__tyhpGeneric(\\Tyhp\\Type::int())()")]
    [InlineData("return $h->zero<int>();", "$h->zero__tyhpGeneric(\\Tyhp\\Type::int())()")]
    [InlineData("return free<int>();", "free__tyhpGeneric(\\Tyhp\\Type::int())()")]
    [InlineData("$h->zero<int>();", "$h->zero__tyhpGeneric(\\Tyhp\\Type::int())()")]
    [InlineData("return [$h->zero<int>()];", "[$h->zero__tyhpGeneric(\\Tyhp\\Type::int())()]")]
    [InlineData("return sink($h->zero<int>());", "sink($h->zero__tyhpGeneric(\\Tyhp\\Type::int())())")]
    [InlineData(
        "return sink(fn(): mixed => $h->zero<int>());",
        "$h->zero__tyhpGeneric(\\Tyhp\\Type::int())()")]
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
    public void Emit_CallSiteWithoutTypeArguments_KeepsTheWrapper()
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

        php.Should().Contain("return $h->zero();");
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
            "function pair__tyhpGeneric(?\\Tyhp\\Type $__generic_TA, ?\\Tyhp\\Type $__generic_TB): \\Closure");
        php.Should().Contain(
            "$h->pair__tyhpGeneric(\\Tyhp\\Type::int(), \\Tyhp\\Type::string())()");
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

        php.Should().Contain("public function name(): string;");
        php.Should().Contain("public function name__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure;");
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
            "public abstract function name__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure;");
        php.Should().NotContain(
            "public abstract function name__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure\n{");
        php.Should().NotContain(
            "public abstract function name__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure\n    {");
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

        php.Should().Contain("public function name(): \\Tyhp\\Promise");
        php.Should().Contain("return $this->name__tyhpGeneric(null)();");
        php.Should().Contain(
            "public function name__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("return function () use ($__generic_T): \\Tyhp\\Promise");
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
            "Checker::isType__tyhpGeneric($this->__tyhpGeneric->resolvedType(\\Accessor::class, 'TValue'))($result)");
    }

    [Fact]
    public void Emit_CallSitePassingOwnMethodGeneric_OutsideVariant_ErasesToMixedType()
    {
        // Callee is Mechanism D; caller has a method generic but is not itself a variant. Passing T
        // as a type argument must not emit `fromClassName(T::class)` — there is no reified binding.
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

        php.Should().Contain("Helper::take__tyhpGeneric(\\Tyhp\\Type::mixed())");
        php.Should().NotContain("fromClassName(\\T::class)");
        php.Should().NotContain("fromClassName(T::class)");
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
    /// Emits <paramref name="tyhp"/>, writes it next to a driver that autoloads the core runtime, and
    /// returns what PHP printed.
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
            var result = compilationService.ParseFiles(files, new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

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
