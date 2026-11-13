using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers the soundness fixes for <c>default()</c> and runtime generic tracking recorded in
/// <c>FOUND_BUGS.md</c> items 1, 3, 5 and 10 — each of which previously compiled with zero errors
/// and produced PHP that crashed or held the wrong value at runtime.
/// </summary>
[Trait("Category", "Checker")]
public class DefaultAndGenericTrackingRuleTests
{
    // Item 10 — default(<class type>) emits `null`, so it must be typed as null.

    [Fact]
    public void Check_DefaultOfClassType_ReturnedAsNonNullable_ReportsIncompatibleReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass { public int $n = 1; }
            class Consumer {
                public static function nonNullableReturn(?MyClass $instance = null): MyClass {
                    return $instance ?? default(MyClass);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_DefaultOfClassType_AssignedToNonNullableProperty_ReportsTypeMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass { public int $n = 1; }
            class Holder {
                public MyClass $value = default(MyClass);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_DefaultOfNullableClassType_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass { public int $n = 1; }
            class Holder {
                public ?MyClass $value = default(?MyClass);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_DefaultOfScalarType_KeepsSpelledType()
    {
        // `default(int)` folds to `0`, so it stays an int rather than becoming null.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                public int $count = default(int);
                public string $label = default(string);
                public bool $flag = default(bool);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    // Item 5 — default(<concrete type>) folds to a literal and is a constant expression.

    [Fact]
    public void Check_NewInPropertyInitializer_Reports4090()
    {
        // A property default is not a context where PHP accepts `new`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            class Host {
                public Bag $bag = new Bag();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInStaticPropertyInitializer_Reports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            class Host {
                public static Bag $bag = new Bag();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInGenericPropertyInitializer_Reports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Coll<T> {}
            class Host {
                public Coll<int> $items = new Coll<int>();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewNestedInPropertyArray_Reports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            class Host {
                public array $items = [new Bag()];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_CloneInPropertyInitializer_Reports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                public mixed $value = clone null;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_StaticClassInPropertyInitializer_Reports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                public string $name = static::class;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_ConstantPropertyInitializer_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                public int $n = 1 + 2;
                public string $label = 'a' . 'b';
                public int $cast = (int) 1;
                public string $kind = self::class;
                public bool $flag = true ? true : false;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_StructInitializerProperty_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct { int $x = 0; int $y = 0; };
            class Host {
                public Point $origin = new Point[];
                public Point $shifted = new Point[x => 1, y => 2];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_StructInitializerSpreadInProperty_Reports4090()
    {
        // A spread lowers to `\array_replace`, which a property default cannot call.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct { int $x = 0; int $y = 0; };
            class Host {
                public Point $shifted = new Point[...[x => 1]];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInParameterDefault_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            function take(Bag $bag = new Bag()): Bag {
                return $bag;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInPromotedParameter_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            class Host {
                public function __construct(public Bag $bag = new Bag()) {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInStaticVariable_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            function remember(): void {
                static $bag = new Bag();
                (void)$bag;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInGlobalConstant_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            const Saved = new Bag();
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInAttributeArgument_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}

            #[\Attribute(\Attribute::TARGET_CLASS)]
            class Mark {
                public function __construct(public Bag $bag) {}
            }

            #[Mark(new Bag())]
            class Host {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Theory]
    [InlineData("public mixed $x = @1;")]
    [InlineData("public bool $x = 1 instanceof Host;")]
    [InlineData("public bool $x = 1 is Host;")]
    [InlineData("public mixed $x = 1 |> \\strlen(...);")]
    [InlineData("public mixed $x = (decimal)1;")]
    [InlineData("public mixed $x = $a::FOO;")]
    [InlineData("public int $x = ++$a;")]
    [InlineData("public int $x = --$a;")]
    [InlineData("public mixed $x = true ? new Bag() : null;")]
    [InlineData("public static mixed $x = static::class;")]
    public void Check_NonConstantFormInPropertyInitializer_Reports4090(string member)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class Bag {}
            class Host {
                {{member}}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Theory]
    [InlineData("const X = new Bag();")]
    [InlineData("const X = [new Bag()];")]
    [InlineData("const X = true ? new Bag() : null;")]
    [InlineData("const X = clone null;")]
    [InlineData("const X = static::class;")]
    [InlineData("const X = 1 instanceof Host;")]
    [InlineData("const X = @1;")]
    public void Check_NonConstantFormInClassConstant_Reports4090(string member)
    {
        // PHP refuses `new` in a class constant as well as in a property default.
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class Bag {}
            class Host {
                {{member}}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewInEnumCaseValue_Reports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag {}
            enum Level: int {
                case Low = new Bag();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Theory]
    [InlineData("const X = @1;")]
    [InlineData("const X = \\strlen('a');")]
    [InlineData("const X = static::class;")]
    [InlineData("const X = 1 instanceof Bag;")]
    public void Check_NonConstantFormInGlobalConstant_Reports4090(string line)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class Bag {}
            {{line}}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_ConstantGlobalConstants_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag { const A = 1; }
            const One = 1;
            const Two = One + 1;
            const Items = [One, Two, Bag::A];
            const Mask = \E_ALL & ~\E_NOTICE;
            const Name = Bag::class;
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_StaticClassInParameterDefault_Reports4090()
    {
        // `static::class` is not a constant expression in any context.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                public function name(string $n = static::class): string {
                    return $n;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_ConstantClassConstants_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                const A = 1;
                const B = self::A + 2;
                const C = [self::A, 'k' => self::class];
                const D = self::A > 0 ? 'a' : 'b';
            }
            enum Level: int {
                case Low = 1;
                case High = 1 + 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_StructInitializerSpreadInParameterDefault_Reports4090()
    {
        // The spread emits `\array_replace`, which no constant expression can call.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct { int $x = 0; int $y = 0; };
            function shift(Point $p = new Point[...[x => 1]]): Point {
                return $p;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NewDynamicClassInPropertyInitializer_StillReports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                public object $bag = new ($GLOBALS['c'])();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_DefaultOfConcreteType_InPropertyInitializer_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                public int $count = default(int);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_DefaultOfConcreteType_InParameterDefault_No4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function withDefault(int $n = default(int)): int {
                return $n;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_DefaultOfGenericParameter_InPropertyInitializer_StillReports4090()
    {
        // The value depends on the type argument bound at construction, so it cannot fold to a
        // literal. Accepting it needs initializer lowering into the constructor (item 7).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box<TReturn> {
                public ?TReturn $value = default(TReturn);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_DefaultOfMethodGenericParameter_InParameterDefault_StillReports4090()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function pick<T>(?T $value = default(T)): ?T {
                return $value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    // Item 1a — typeof(<class generic>) has no instance to read from inside a static member.

    [Fact]
    public void Check_TypeofClassGenericInStaticMethod_Reports4148()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticTypeof(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericTypeofInStaticContext);
    }

    [Fact]
    public void Check_TypeofClassGenericInInstanceMethod_No4148()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public function instanceTypeof(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericTypeofInStaticContext);
    }

    [Fact]
    public void Check_TypeofDeclaredClassInStaticMethod_No4148()
    {
        // `typeof(Sample)` names a real class and folds at compile time, so `static` is irrelevant.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Sample {}
            class Holder<T> {
                public static function ofSample(): \Tyhp\Type {
                    return typeof(Sample);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericTypeofInStaticContext);
    }

    // Item 4 — default(<class generic>) has the same instance requirement as typeof.

    [Fact]
    public void Check_DefaultClassGenericInStaticMethod_Reports4152()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticZero(): mixed {
                    return default(T);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericDefaultInStaticContext);
    }

    [Fact]
    public void Check_DefaultClassGenericInInstanceMethod_No4152()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public function instanceZero(): mixed {
                    return default(T);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericDefaultInStaticContext);
    }

    /// <summary>
    /// A generic the method declares itself shadows the class's and is served by the Mechanism D
    /// binder parameter, which a static method has just as much as an instance one.
    /// </summary>
    [Fact]
    public void Check_DefaultOwnGenericShadowingClassGenericInStaticMethod_No4152()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticZero<T>(): mixed {
                    return default(T);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericDefaultInStaticContext);
    }

    [Fact]
    public void Check_DefaultOfConcreteTypeInStaticMethod_No4152()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticZero(): int {
                    return default(int);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericDefaultInStaticContext);
    }

    // Item 37 (reify, not reject) — `instanceof T` / `is T` against a class generic has the same
    // "nothing on the instance to read from" problem as typeof/default in a static member, so it gets
    // the same TYHP4156 reject rather than silently reifying to a hard-coded `false`.

    [Fact]
    public void Check_InstanceofClassGenericInStaticMethod_Reports4156()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticCheck(mixed $value): bool {
                    return $value instanceof T;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericInstanceofInStaticContext);
    }

    [Fact]
    public void Check_IsAliasAgainstClassGenericInStaticMethod_Reports4156()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticCheck(mixed $value): bool {
                    return $value is T;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericInstanceofInStaticContext);
    }

    [Fact]
    public void Check_InstanceofClassGenericInInstanceMethod_No4156()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public function instanceCheck(mixed $value): bool {
                    return $value instanceof T;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericInstanceofInStaticContext);
    }

    [Fact]
    public void Check_InstanceofDeclaredClassInStaticMethod_No4156()
    {
        // `instanceof Sample` names a real class, so `static` is irrelevant — same precedence
        // TyhpEmitter.TryBuildReifiedInstanceofCheck gives a declared class over a generic name.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Sample {}
            class Holder<T> {
                public static function ofSample(mixed $value): bool {
                    return $value instanceof Sample;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericInstanceofInStaticContext);
    }

    /// <summary>
    /// A generic the method declares itself shadows the class's and is served by the Mechanism D
    /// binder parameter, which a static method has just as much as an instance one.
    /// </summary>
    [Fact]
    public void Check_InstanceofOwnGenericShadowingClassGenericInStaticMethod_No4156()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T> {
                public static function staticCheck<T>(mixed $value): bool {
                    return $value instanceof T;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericInstanceofInStaticContext);
    }

    // Item 3 — a variadic constructor on a runtime-tracked generic class used to be rejected outright
    // (the retired TYHP4149), because type arguments arrived as hidden constructor parameters and PHP
    // allows only one variadic, in last position. Mechanism C routes them through
    // `__initGenerics__tyhpGeneric` instead, so the combination is now legal and must compile clean.
    // The behavior of the emitted code is pinned by MechanismCEmitterTests.

    [Fact]
    public void Check_TrackedGenericClassWithVariadicConstructor_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag<T> {
                public function __construct(T ...$items): void {}
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    // Item 1 — a callable that needs its own generics at runtime is emitted alongside a
    // `__tyhpGeneric` variant, so that name is reserved and overrides must keep the parameters the
    // caller passes.

    [Theory]
    [InlineData("zero__tyhpGeneric")]
    [InlineData("zero__TYHPGENERIC")]
    [InlineData("zero__TyhpGeneric")]
    public void Check_MethodNamedLikeAGenericVariant_Reports4150(string methodName)
    {
        // PHP matches method names case-insensitively, so every casing collides.
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class Holder {
                public function {{methodName}}(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedGenericVariantSuffix);
    }

    [Fact]
    public void Check_FunctionNamedLikeAGenericVariant_Reports4150()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function zero__tyhpGeneric(): void {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedGenericVariantSuffix);
    }

    [Fact]
    public void Check_NameMerelyContainingTheSuffix_No4150()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                public function tyhpGenericHelper(): void {}
                public function generic(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerReservedGenericVariantSuffix);
    }

    [Theory]
    [InlineData("public function pick(): mixed { return null; }")]
    [InlineData("public function pick<U>(): mixed { return default(U); }")]
    public void Check_OverrideDroppingOrRenamingGenerics_Reports4151(string overrideDecl)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class Base {
                public function pick<T>(): mixed { return default(T); }
            }
            class Child extends Base {
                {{overrideDecl}}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericOverrideParameterMismatch);
    }

    [Fact]
    public void Check_OverrideReorderingGenerics_Reports4151()
    {
        // Type arguments are passed positionally, so the order is part of the contract.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function pair<TA, TB>(): mixed { return default(TA); }
            }
            class Child extends Base {
                public function pair<TB, TA>(): mixed { return default(TB); }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericOverrideParameterMismatch);
    }

    [Fact]
    public void Check_OverrideKeepingGenerics_No4151()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function pick<T>(): mixed { return default(T); }
            }
            class Child extends Base {
                public function pick<T>(): mixed { return default(T); }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericOverrideParameterMismatch);
    }

    [Fact]
    public void Check_OverrideOfNonGenericMethod_No4151()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function plain(): mixed { return null; }
            }
            class Child extends Base {
                public function plain(): mixed { return null; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerGenericOverrideParameterMismatch);
    }

    [Fact]
    public void Check_OverrideInheritedThroughAnIntermediateClass_Reports4151()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function pick<T>(): mixed { return default(T); }
            }
            class Middle extends Base {
                public function pick<T>(): mixed { return default(T); }
            }
            class Leaf extends Middle {
                public function pick(): mixed { return null; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerGenericOverrideParameterMismatch);
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
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
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
