using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A callee's type-parameter constraint is resolved in the callee's own type-parameter scope, never
/// by name in the caller's. Caller and callee type parameters that share a spelling must not be
/// confused with one another.
/// </summary>
[Trait("Category", "Checker")]
public class GenericConstraintScopeTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class Box<T extends __Awaited<void|mixed>> {
            public function __construct(public T $value) {}
        }

        final class Pair<A, B extends array<A>> {
            public function __construct(public A $first, public B $rest) {}
        }

        final class Counted<T extends \Countable> {
            public function __construct(public T $item) {}
        }
        """;

    private static IReadOnlyList<string> Errors(string body) =>
        SnippetEmit.Compile(Prelude + "\n" + body).Errors;

    private static void AssertCompiles(string body)
    {
        var errors = Errors(body);
        errors.Should().BeEmpty(string.Join("; ", errors));
    }

    private static void AssertRejectedWithCode(string body, int code)
    {
        var errors = Errors(body);
        errors.Should().Contain(e => e.StartsWith(code + ":"), "errors were: " + string.Join("; ", errors));
    }

    // ---- __Awaited bound (TYHP4373) ----

    [Fact]
    public void AwaitedBound_FunctionTypeParameterSharingTheClassName_Compiles()
    {
        AssertCompiles("""
            function keep<T extends __Awaited<void|mixed>>(Box<T> $b): Box<T> {
                return $b;
            }
            """);
    }

    [Fact]
    public void AwaitedBound_MethodTypeParameterSharingTheClassName_Compiles()
    {
        AssertCompiles("""
            final class Holder<T extends __Awaited<void|mixed>> {
                public function wrap<T extends __Awaited<void|mixed>>(T $v): Box<T> {
                    return new Box<T>($v);
                }

                public function same(Box<T> $b): Box<T> {
                    return $b;
                }
            }
            """);
    }

    [Fact]
    public void AwaitedBound_CallerTypeParameterCalleeSameName_Compiles()
    {
        AssertCompiles("""
            function wrap<T extends __Awaited<void|mixed>>(T $v): Box<T> {
                return new Box<T>($v);
            }

            function viaCaller<T extends __Awaited<void|mixed>>(T $v): Box<T> {
                return wrap($v);
            }
            """);
    }

    [Fact]
    public void AwaitedBound_UnconstrainedCallerTypeParameter_StillReports4373()
    {
        AssertRejectedWithCode("""
            function viaOpen<T>(T $v): Box<T> {
                return new Box<T>($v);
            }
            """, 4373);
    }

    // ---- constraint that names a sibling type parameter (callee T vs caller T) ----

    [Fact]
    public void SiblingConstraint_CallerTypeParameterSharesTheCalleeSiblingName_Compiles()
    {
        AssertCompiles("""
            function mk<T, U extends array<T>>(T $first, U $rest): Pair<T, U> {
                return new Pair<T, U>($first, $rest);
            }

            function caller<T>(int $first, array<int> $rest): Pair<int, array<int>> {
                return mk($first, $rest);
            }
            """);
    }

    [Fact]
    public void SiblingConstraint_ExplicitCallSiteArguments_Compile()
    {
        AssertCompiles("""
            function mk<T, U extends array<T>>(T $first, U $rest): Pair<T, U> {
                return new Pair<T, U>($first, $rest);
            }

            function caller<T>(): Pair<int, array<int>> {
                return mk<int, array<int>>(1, [2]);
            }
            """);
    }

    [Fact]
    public void SiblingConstraint_NewOnClassFromCallerSharingTheName_Compiles()
    {
        AssertCompiles("""
            function caller<A>(): Pair<int, array<int>> {
                return new Pair<int, array<int>>(1, [2]);
            }
            """);
    }

    [Fact]
    public void SiblingConstraint_ViolationIsStillReported()
    {
        AssertRejectedWithCode("""
            function mk<T, U extends array<T>>(T $first, U $rest): Pair<T, U> {
                return new Pair<T, U>($first, $rest);
            }

            function caller<T>(): void {
                mk<int, array<string>>(1, ['x']);
            }
            """, 4035);
    }

    // ---- callee parameter types on a receiver whose class parameter shares the caller's name ----

    [Fact]
    public void Constructor_ConcreteArgumentsFromCallerSharingTheClassParameterName_Compile()
    {
        AssertCompiles("""
            function make<T extends __Awaited<void|mixed>>(): Box<int> {
                return new Box<int>(1);
            }
            """);
    }

    [Fact]
    public void Constructor_UnconstrainedCallerTypeParameterNamedLikeTheAwaitedClassParameter_NoFalse4373()
    {
        var errors = Errors("""
            function foo<T>(): void {
                Box<int> $explicit = new Box<int>(1);
                Box<int> $inferred = new Box(2);
            }
            """);
        errors.Should().NotContain(e => e.StartsWith("4373:") || e.StartsWith("4010:"),
            "errors were: " + string.Join("; ", errors));
    }

    [Fact]
    public void Constructor_ConcreteArgumentsFromMethodSharingTheClassParameterName_Compile()
    {
        AssertCompiles("""
            final class Maker<T extends __Awaited<void|mixed>> {
                public function make(): Box<string> {
                    return new Box<string>('s');
                }
            }
            """);
    }

    [Fact]
    public void Method_ReceiverInstantiationFromCallerSharingTheClassParameterName_Compiles()
    {
        AssertCompiles("""
            final class Slot<T> {
                public function __construct(private T $value) {}
                public function put(T $v): void {}
                public function get(): T { return $this->value; }
            }

            function useSlot<T>(Slot<int> $s): int {
                $s->put(2);
                return $s->get();
            }
            """);
    }

    [Fact]
    public void StaticFactory_ReceiverInstantiationFromCallerSharingTheClassParameterName_Compiles()
    {
        AssertCompiles("""
            final class Slot<T> {
                public function __construct(private T $value) {}
                public static function of<T>(T $v): Slot<T> { return new Slot<T>($v); }
            }

            function build<T>(): Slot<int> {
                return Slot::of<int>(3);
            }
            """);
    }

    [Fact]
    public void Constructor_WrongArgumentStillReportedWhenNamesCollide()
    {
        AssertRejectedWithCode("""
            function make<A>(): void {
                $p = new Pair<int, array<int>>('x', [2]);
            }
            """, 4010);
    }

    [Fact]
    public void FreeFunction_ParameterNamingAClassIsNotShadowedByTheCallersTypeParameter()
    {
        AssertCompiles("""
            final class Item {}

            function label(Item $i): string {
                return 'item';
            }

            function caller<Item>(): string {
                return label(new \Probe\Item());
            }
            """);
    }

    // ---- ordinary constraint (TYHP4035) with colliding names ----

    [Fact]
    public void ClassConstraint_CallerTypeParameterWithSameNameAndSatisfyingBound_Compiles()
    {
        AssertCompiles("""
            function count<T extends \Countable>(Counted<T> $c): Counted<T> {
                return $c;
            }
            """);
    }

    [Fact]
    public void ClassConstraint_CallerTypeParameterWithSameNameAndMissingBound_Reports4035()
    {
        AssertRejectedWithCode("""
            function open<T>(T $t): Counted<T> {
                return new Counted<T>($t);
            }
            """, 4035);
    }
}
