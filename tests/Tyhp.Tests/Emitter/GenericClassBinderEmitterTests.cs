using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Generated <c>new()</c> and <c>type()</c> on generic classes.
/// </summary>
[Trait("Category", "Emitter")]
public class GenericClassBinderEmitterTests
{
    [Fact]
    public void Emit_ConcreteGeneric_EmitsPublicNewAndType()
    {
        var php = Compile("""
            <?tyhp
            class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain("public static function new(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("public static function type(\\Tyhp\\TypeLike|string|null $T = null): \\Tyhp\\Type");
        php.Should().Contain("binder: \"new\"");
        php.Should().Contain("layouts: [2]");
        php.Should().Contain("Box::new($T)($n)");
        php.Should().Contain("@return \\Closure(int $n): \\Box<T>");
        php.Should().Contain("Box::type($T)");
        php.Should().Contain("@return \\Tyhp\\Type<\\Box<T>>");
        php.Should().Contain("$__tyhpType_T = \\Tyhp\\Type::arg($T);");
        php.Should().Contain("$__tyhp_reflected = new \\ReflectionClass(static::class);");
        php.Should().Contain("$__tyhp_obj->__initGenerics__tyhpGeneric($__tyhpType_T);");
        php.Should().Contain("$__tyhp_obj->__construct($n);");
        php.Should().Contain("$T = \\Tyhp\\Type::arg($T) ?? \\Tyhp\\Type::mixed();");
        php.Should().Contain("return \\Tyhp\\Type::generic(static::class, $T);");
        php.Should().NotContain("final public static function new(");
        php.Should().NotContain("final public static function type(");
        php.Should().Contain("final public static function new_Box__tyhpGeneric(");
    }

    [Fact]
    public void Emit_AbstractGeneric_EmitsTypeOnly()
    {
        var php = Compile("""
            <?tyhp
            abstract class Box<T> {
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain("public static function type(\\Tyhp\\TypeLike|string|null $T = null): \\Tyhp\\Type");
        php.Should().NotContain("function new(");
        php.Should().NotContain("new_Box__tyhpGeneric");
    }

    [Fact]
    public void Emit_TwoParameterGeneric_NamesBothParameters()
    {
        var php = Compile("""
            <?tyhp
            class Map<TKey, TValue> {
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }
            """);

        const string parameters =
            "\\Tyhp\\TypeLike|string|null $TKey = null, \\Tyhp\\TypeLike|string|null $TValue = null";
        php.Should().Contain($"public static function new({parameters}): \\Closure");
        php.Should().Contain($"public static function type({parameters}): \\Tyhp\\Type");
        php.Should().Contain("Map::new($TKey, $TValue)()");
        php.Should().Contain("@return \\Tyhp\\Type<\\Map<TKey, TValue>>");
        php.Should().Contain("$__tyhp_obj->__initGenerics__tyhpGeneric($__tyhpType_TKey, $__tyhpType_TValue);");
    }

    [Theory]
    [InlineData("private", "private static")]
    [InlineData("protected", "protected static")]
    public void Emit_NewVisibility_MatchesConstructorAndIsNotFinal(string visibility, string modifiers)
    {
        var php = Compile($$"""
            <?tyhp
            class Box<T> {
                {{visibility}} function __construct(int $id) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain($"{modifiers} function new(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("public static function type(\\Tyhp\\TypeLike|string|null $T = null): \\Tyhp\\Type");
        php.Should().NotContain("final private static function new(");
        php.Should().NotContain("final protected static function new(");
        php.Should().NotContain("final public static function new(");
        php.Should().NotContain("final public static function type(");
        if (visibility == "private")
        {
            php.Should().Contain("private static function new_Box__tyhpGeneric(");
            php.Should().NotContain("final private static function");
        }
        else
        {
            php.Should().Contain("final protected static function new_Box__tyhpGeneric(");
        }
    }

    [Fact]
    public void Emit_GenericChild_RedeclaresNewAndTypeWithItsOwnParameters()
    {
        var php = Compile("""
            <?tyhp
            class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }

            class Child<A, B> extends Box<A> {
                public function other(): \Tyhp\Type {
                    return typeof(B);
                }
            }
            """);

        php.Should().Contain("public static function new(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain(
            "public static function new(\\Tyhp\\TypeLike|string|null $A = null, \\Tyhp\\TypeLike|string|null $B = null): \\Closure");
        php.Should().Contain(
            "public static function type(\\Tyhp\\TypeLike|string|null $A = null, \\Tyhp\\TypeLike|string|null $B = null): \\Tyhp\\Type");
        php.Should().NotContain("final public static function new(");
        php.Should().NotContain("final public static function type(");
    }

    [Fact]
    public void Emit_ChildWithFewerParameters_RedeclaresWithInheritedTail()
    {
        var php = Compile("""
            <?tyhp
            class Map<TKey, TValue> {
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }

            class Half<X> extends Map<X, int> {}

            class Quarter<Y> extends Half<Y> {}
            """);

        const string tail = "mixed ...$__tyhpInherited";
        php.Should().Contain(
            "public static function new(\\Tyhp\\TypeLike|string|null $X = null, " + tail + "): \\Closure");
        php.Should().Contain(
            "public static function type(\\Tyhp\\TypeLike|string|null $X = null, " + tail + "): \\Tyhp\\Type");
        // A child of a redeclaration that ends in a variadic has to end in one too.
        php.Should().Contain(
            "public static function new(\\Tyhp\\TypeLike|string|null $Y = null, " + tail + "): \\Closure");
        CountOf(php, "function new(").Should().Be(3);
        CountOf(php, tail).Should().Be(4);
    }

    [Fact]
    public void Emit_PublicNew_GuardsSubclassConstructorUnlessFinal()
    {
        var open = Compile("""
            <?tyhp
            class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);
        open.Should().Contain("static::class !== self::class");
        open.Should().Contain("getConstructor()?->isPublic() === false");

        var closed = Compile("""
            <?tyhp
            final class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);
        closed.Should().NotContain("isPublic()");

        var protectedNew = Compile("""
            <?tyhp
            class Box<T> {
                protected function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);
        protectedNew.Should().NotContain("isPublic()");
    }

    [Fact]
    public void Run_InheritedNew_NeverBuildsAClassWithNonPublicConstructor()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Vendor;

            class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }

            class Secret extends Box<int> {
                private function __construct(public int $n) {}
            }

            class Guarded extends Box<int> {
                protected function __construct(public int $n) {}
            }

            class Map<TKey, TValue> {
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }

            class Half<X> extends Map<X, int> {}

            class Defaulted<T = int> {
                public function __construct() {}
            }

            class HalfFactory {
                public static function make(): Half<string> {
                    return new Half<string>();
                }
            }
            """, "8.4");

        result.Errors.Should().BeEmpty(string.Join("\n", result.Errors));
        var output = EmittedPhpRunner.RunAgainstRuntimeDist(result.Files!, """
            function attempt(string $label, callable $call): void {
                try {
                    $result = (string) $call();
                } catch (\Throwable $e) {
                    $result = $e::class;
                }

                echo $label, "=", $result, "\n";
            }

            attempt('secret', fn() => \Vendor\Secret::new()(1)::class);
            attempt('guarded', fn() => \Vendor\Guarded::new()(1)::class);
            attempt('box', fn() => \Vendor\Box::new()(1)::class);
            attempt('half-new', fn() => \Tyhp\Type::of(\Vendor\Half::new(\Tyhp\Type::string)()));
            attempt('half-type', fn() => \Vendor\Half::type(\Tyhp\Type::string));
            attempt('half-tyhp', fn() => \Tyhp\Type::of(\Vendor\HalfFactory::make()));
            attempt('half-named', fn() => \Tyhp\Type::generic(\Vendor\Half::class, X: \Tyhp\Type::string));
            attempt('default', fn() => \Vendor\Defaulted::type());
            attempt('unbound', fn() => \Vendor\Box::type());
            """, "core");
        output.Should().NotBeNull();
        output!.TrimEnd().Should().Be("""
            secret=Error
            guarded=Error
            box=Vendor\Box
            half-new=Vendor\Half<string>
            half-type=Vendor\Half<string>
            half-tyhp=Vendor\Half<string>
            half-named=Vendor\Half<string>
            default=Vendor\Defaulted<int>
            unbound=Vendor\Box<mixed>
            """.TrimEnd());
    }

    [Fact]
    public void Emit_NarrowerConstructor_InheritsNew()
    {
        var php = Compile("""
            <?tyhp
            class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }

            class Secret<T> extends Box<T> {
                private function __construct(public int $n) {}

                public function other(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain("private function __construct(public int $n)");
        php.Should().Contain("public static function new(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().NotContain("private static function new(");
        CountOf(php, "function new(").Should().Be(1);
        CountOf(php, "function type(").Should().Be(2);
    }

    [Fact]
    public void Emit_InterfaceAndTrait_GetNeither()
    {
        var php = Compile("""
            <?tyhp
            interface Reader<T> {
                public function read(): T;
            }

            trait Holder<T> {
                public function id(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().NotContain("function new(");
        php.Should().NotContain("function type(");
    }

    [Fact]
    public void Run_NewTypeAndIntBox_ConstructAndCompareTypes()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Vendor;

            class Box<T> {
                public function __construct(public int $n) {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }

            class IntBox extends Box<int> {}

            class Child<A, B> extends Box<A> {
                public function other(): \Tyhp\Type {
                    return typeof(B);
                }
            }

            class Map<TKey, TValue> {
                public function describe(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }
            """, "8.4");

        result.Errors.Should().BeEmpty();
        var output = EmittedPhpRunner.RunAgainstRuntimeDist(result.Files!, """
            $box = \Vendor\Box::new(\Tyhp\Type::int)(42);
            echo "box-class=", $box::class, "\n";
            echo "box-n=", $box->n, "\n";
            echo "box-type=", \Tyhp\Type::of($box), "\n";

            $intBox = \Vendor\IntBox::new()(5);
            echo "int-class=", $intBox::class, "\n";
            echo "int-n=", $intBox->n, "\n";

            $typed = \Vendor\Box::type(\Tyhp\Type::int);
            $generic = \Tyhp\Type::generic(\Vendor\Box::class, \Tyhp\Type::int);
            $parsed = \Tyhp\Type::parse('Vendor\\Box<int>');
            $builder = \Tyhp\Type::generic(\Vendor\Box::class);
            echo "type-eq-generic=", \Tyhp\Type::compatible($typed, $generic) ? "yes" : "no", "\n";
            echo "generic-eq-type=", \Tyhp\Type::compatible($generic, $typed) ? "yes" : "no", "\n";
            echo "type-eq-parse=", \Tyhp\Type::compatible($typed, $parsed) ? "yes" : "no", "\n";
            echo "parse-eq-type=", \Tyhp\Type::compatible($parsed, $typed) ? "yes" : "no", "\n";
            echo "typed=", $typed, "\n";
            echo "builder=", $builder->getKind(), "\n";

            $nested = \Vendor\Map::type(\Tyhp\Type::string, \Vendor\Box::type(\Tyhp\Type::int));
            $nestedGeneric = \Tyhp\Type::generic(
                \Vendor\Map::class,
                \Tyhp\Type::string,
                \Vendor\Box::type(\Tyhp\Type::int),
            );
            echo "nested=", $nested, "\n";
            echo "nested-eq=", \Tyhp\Type::compatible($nested, $nestedGeneric) ? "yes" : "no", "\n";

            $child = \Vendor\Child::new(\Tyhp\Type::int, \Tyhp\Type::string)(7);
            echo "child-class=", $child::class, "\n";
            echo "child-n=", $child->n, "\n";
            echo "child-type=", \Tyhp\Type::of($child), "\n";
            """, "core");
        output.Should().NotBeNull();
        output!.TrimEnd().Should().Be("""
            box-class=Vendor\Box
            box-n=42
            box-type=Vendor\Box<int>
            int-class=Vendor\IntBox
            int-n=5
            type-eq-generic=yes
            generic-eq-type=yes
            type-eq-parse=yes
            parse-eq-type=yes
            typed=Vendor\Box<int>
            builder=generic
            nested=Vendor\Map<string, Vendor\Box<int>>
            nested-eq=yes
            child-class=Vendor\Child
            child-n=7
            child-type=Vendor\Child<int, string>
            """.TrimEnd());
    }

    private static string Compile(string tyhp)
    {
        var result = SnippetEmit.Compile(tyhp, "8.4");
        result.Errors.Should().BeEmpty(string.Join("\n", result.Errors));
        return result.Php;
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
