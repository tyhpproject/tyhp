using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// An inline <c>fn</c> passed to <c>PropertyPath&lt;callable(T): R&gt;</c> binds a generic <c>R</c>
/// from the property chain's type, the same way it does for <c>Expression&lt;callable(T): R&gt;</c>.
/// </summary>
[Trait("Category", "Checker")]
public class PropertyPathResultInferenceTests
{
    private const string Prelude = """
        <?tyhp
        use Tyhp\PropertyPath;

        final class Address {
            public string $city = '';
            public int $zip = 0;
        }
        final class Person {
            public string $name = '';
            public Address $address;
            public ?Person $boss = null;
            public function __construct() { $this->address = new Address(); }
        }
        final class Column<T> {}
        final class Columns<T> {
            public function column<R>(PropertyPath<callable(T): R> $path): Column<R> {
                return new Column<R>();
            }
        }
        function pick<R>(PropertyPath<callable(Person): R> $path, Person $p): R {
            return $path($p);
        }
        function pickEither<R>(PropertyPath<callable(Person): R>|(callable(Person): R) $path, Person $p): R {
            return $path($p);
        }
        function pickExprEither<R>(\Tyhp\Expression<callable(Person): R>|(callable(Person): R) $path, Person $p): R {
            return $path($p);
        }
        """;

    [Fact]
    public void SimplePath_InfersFunctionGeneric()
    {
        var errors = Errors("function run(Person $p): string { return pick(fn ($x) => $x->name, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void SimplePath_WithAnnotatedParameter_InfersFunctionGeneric()
    {
        var errors = Errors("function run(Person $p): string { return pick(fn (Person $x) => $x->name, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ChainedPath_InfersLastSegmentType()
    {
        var errors = Errors("function run(Person $p): int { return pick(fn ($x) => $x->address->zip, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ChainedPath_InfersMethodGenericOnGenericReceiver()
    {
        var errors = Errors("""
            function run(Columns<Person> $c): Column<string> {
                return $c->column(fn ($x) => $x->address->city);
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void InferredResult_IsNotMixed()
    {
        var errors = Errors("function run(Person $p): int { return pick(fn ($x) => $x->name, $p); }");

        errors.Should().ContainSingle(e => e.StartsWith("4009") && e.Contains("`string`") && e.Contains("`int`"));
    }

    [Fact]
    public void NullSafePath_InfersNullableResult()
    {
        var errors = Errors("function run(Person $p): ?string { return pick(fn ($x) => $x->boss?->name, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void NullSafePath_NullableResultIsNotAssignableToNonNullable()
    {
        var errors = Errors("function run(Person $p): string { return pick(fn ($x) => $x->boss?->name, $p); }");

        errors.Should().ContainSingle(e => e.StartsWith("4009") && e.Contains("`?string`"));
    }

    [Fact]
    public void UnionWithCallable_InfersFunctionGeneric()
    {
        var errors = Errors("function run(Person $p): string { return pickEither(fn ($x) => $x->name, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void UnionWithCallable_InferredResultIsNotMixed()
    {
        var errors = Errors("function run(Person $p): int { return pickEither(fn ($x) => $x->name, $p); }");

        errors.Should().ContainSingle(e => e.StartsWith("4009") && e.Contains("`string`") && e.Contains("`int`"));
    }

    [Fact]
    public void ExpressionUnionWithCallable_InfersFunctionGeneric()
    {
        var errors = Errors("function run(Person $p): string { return pickExprEither(fn ($x) => $x->name, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitResultTypeArgument_StillAccepted()
    {
        var errors = Errors("function run(Person $p): string { return pick<string>(fn ($x) => $x->name, $p); }");

        errors.Should().BeEmpty();
    }

    [Fact]
    public void MismatchedExplicitResultTypeArgument_StillErrors()
    {
        var errors = Errors("function run(Person $p): int { return pick<int>(fn ($x) => $x->name, $p); }");

        errors.Should().NotBeEmpty();
        errors.Should().NotContain(e => e.StartsWith("4377"));
    }

    private static IReadOnlyList<string> Errors(string function)
        => SnippetEmit.Compile(Prelude + "\n" + function).Errors;
}
