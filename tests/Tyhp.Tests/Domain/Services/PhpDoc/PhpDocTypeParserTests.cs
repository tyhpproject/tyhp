using Tyhp.Domain.Services.PhpDoc;

namespace Tyhp.Tests.Domain.Services.PhpDoc;

[Trait("Category", "Domain")]
[Trait("Category", "PhpDoc")]
public class PhpDocTypeParserTests
{
    [Theory]
    [InlineData("string", "string")]
    [InlineData("int", "int")]
    [InlineData(@"\Foo\Bar", @"\Foo\Bar")]
    [InlineData("?string", "?string")]
    [InlineData("string|int", "string|int")]
    [InlineData("(int|string)", "int|string")]
    [InlineData("string | int | null", "string|int|null")]
    [InlineData("DateTime/DateTimeImmutable", "DateTime|DateTimeImmutable")]
    [InlineData("A&B", "A&B")]
    [InlineData("(A&B)|null", "(A&B)|null")]
    [InlineData("array<(int|string), T>", "array<int|string, T>")]
    [InlineData("array<string, mixed>", "array<string, mixed>")]
    [InlineData("list<int>", "list<int>")]
    [InlineData("Collection<T>", "Collection<T>")]
    [InlineData(@"\Generator<int, string>", @"\Generator<int, string>")]
    [InlineData("iterable<string, Foo>", "iterable<string, Foo>")]
    [InlineData("?array<string, int>", "?array<string, int>")]
    public void Normalize_PreservesRepresentableTypes(string input, string expected)
    {
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("integer", "int")]
    [InlineData("boolean", "bool")]
    [InlineData("double", "float")]
    [InlineData("long", "int")]
    [InlineData("INTEGER", "int")]
    [InlineData("integer|boolean", "int|bool")]
    [InlineData("array<integer, boolean>", "array<int, bool>")]
    public void Normalize_RewritesPhpDocScalarAliases(string input, string expected)
    {
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("string[]", "array<int, string>")]
    [InlineData("int[]", "array<int, int>")]
    [InlineData("(string|int)[]", "array<int, string|int>")]
    [InlineData("string[][]", "array<int, array<int, string>>")]
    [InlineData("?int[]", "?array<int, int>")]
    public void Normalize_ArrayPostfix_BecomesIntKeyedArray(string input, string expected)
    {
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("array{name: string, age: int}", "array")]
    [InlineData("array{0: string, 1?: int}", "array")]
    [InlineData("{name: string}", "array")]
    [InlineData("array{foo: array{bar: int}}", "array")]
    [InlineData("object{foo: string}", "object")]
    public void Normalize_ArrayShapes_BecomeArrayOrObject(string input, string expected)
    {
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("callable(int, string): bool", "callable")]
    [InlineData("callable(): void", "callable")]
    [InlineData(@"\Closure(int): string", "callable")]
    [InlineData("callable(int &$x, string ...$rest): mixed", "callable")]
    [InlineData("callable", "callable")]
    public void Normalize_CallableSignatures_BecomeCallable(string input, string expected)
    {
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("class-string<T>", "string")]
    [InlineData("class-string<Foo\\Bar>", "string")]
    [InlineData("class-string", "string")]
    public void Normalize_ClassString_BecomesString(string input, string expected)
    {
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("'foo'", "string")]
    [InlineData("'foo'|'bar'", "string")]
    [InlineData("0|1", "int")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("-1", "int")]
    [InlineData("0xFF", "int")]
    [InlineData("3.14", "float")]
    public void Normalize_Literals_MapToUnderlyingScalarType(string input, string expected)
    {
        // The tyhpdef type grammar (`typeWithoutStatic`) has no quoted-string or numeric
        // literal type syntax, only `array` / `callable` / `name`. `true` and `false` are
        // themselves valid tyhpdef type names, so those two round-trip unchanged.
        PhpDocTypeParser.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("int<0, max>")]
    [InlineData("key-of<Foo>")]
    [InlineData("non-empty-string")]
    [InlineData("positive-int")]
    [InlineData("Foo::BAR")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("array<")]
    [InlineData("string|")]
    public void Normalize_Unsupported_BecomesMixed(string input)
    {
        PhpDocTypeParser.Normalize(input).Should().Be("mixed");
    }

    [Fact]
    public void Normalize_NestedGenerics()
    {
        PhpDocTypeParser.Normalize("array<string, array<int, Foo<Bar, Baz>>>")
            .Should().Be("array<string, array<int, Foo<Bar, Baz>>>");
    }

    [Fact]
    public void Normalize_UnionWithShape_CollapsesShapeOnly()
    {
        PhpDocTypeParser.Normalize("Foo|array{name: string}")
            .Should().Be("Foo|array");
    }

    [Fact]
    public void Normalize_UnionWithUnrepresentable_BecomesMixed()
    {
        PhpDocTypeParser.Normalize("string|int<0, max>")
            .Should().Be("mixed");
    }

    [Fact]
    public void Normalize_NullableGroupedIntersection()
    {
        PhpDocTypeParser.Normalize("?(A&B)")
            .Should().Be("?(A&B)");
    }

    [Fact]
    public void TryConsume_LeavesRemainderAfterType()
    {
        PhpDocTypeParser.TryConsume("array<string, int> $items extra", out var normalized, out var remainder)
            .Should().BeTrue();
        normalized.Should().Be("array<string, int>");
        remainder.TrimStart().Should().StartWith("$items");
    }

    [Fact]
    public void TryConsume_CallableLeavesParameterName()
    {
        PhpDocTypeParser.TryConsume("callable(int, string): bool $callback", out var normalized, out var remainder)
            .Should().BeTrue();
        normalized.Should().Be("callable");
        remainder.TrimStart().Should().StartWith("$callback");
    }

    [Fact]
    public void TryParseArrayShape_KeyedAndOptionalFields()
    {
        PhpDocTypeParser.TryParseArrayShape(
                """
                array{
                    index: string,
                    type?: string,
                    ignore_error: bool
                }
                """,
                out var fields)
            .Should().BeTrue();
        fields.Should().HaveCount(3);
        fields[0].Key.Should().Be("index");
        fields[0].Type.Should().Be("string");
        fields[0].Optional.Should().BeFalse();
        fields[1].Key.Should().Be("type");
        fields[1].Optional.Should().BeTrue();
        fields[2].Key.Should().Be("ignore_error");
        fields[2].Type.Should().Be("bool");
    }

    [Fact]
    public void TryParseArrayShape_PositionalAndQuotedKeys()
    {
        PhpDocTypeParser.TryParseArrayShape("array{string, int}", out var tuple)
            .Should().BeTrue();
        tuple.Should().HaveCount(2);
        tuple[0].Key.Should().Be("0");
        tuple[0].KeyIsInteger.Should().BeTrue();
        tuple[0].Type.Should().Be("string");
        tuple[1].Key.Should().Be("1");
        tuple[1].Type.Should().Be("int");

        PhpDocTypeParser.TryParseArrayShape("array{'foo-bar': int, 2: bool}", out var mixed)
            .Should().BeTrue();
        mixed.Should().HaveCount(2);
        mixed[0].Key.Should().Be("foo-bar");
        mixed[0].KeyIsQuotedString.Should().BeTrue();
        mixed[1].Key.Should().Be("2");
        mixed[1].KeyIsInteger.Should().BeTrue();
    }

    [Fact]
    public void TryParseArrayShape_NestedShapeAndNonShape()
    {
        PhpDocTypeParser.TryParseArrayShape("array{nested: array{x: int}}", out var fields)
            .Should().BeTrue();
        fields.Should().ContainSingle();
        fields[0].NestedShape.Should().NotBeNull();
        fields[0].NestedShape![0].Key.Should().Be("x");
        fields[0].NestedShape[0].Type.Should().Be("int");

        PhpDocTypeParser.TryParseArrayShape("array<string, int>", out _)
            .Should().BeFalse();
        PhpDocTypeParser.TryParseArrayShape("int|string", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryConsume_DollarNameIsNotAType()
    {
        PhpDocTypeParser.TryConsume("$foo desc", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Normalize_WhitespaceAroundOperators()
    {
        PhpDocTypeParser.Normalize(" A & B | C ").Should().Be("(A&B)|C");
    }

    [Theory]
    [InlineData("array <p>Returns a backtrace.</p>")]
    [InlineData("array<p>Returns a backtrace.</p>")]
    [InlineData("array <b>list of drivers</b>")]
    [InlineData("array <p class=\"para\">Returns a backtrace.</p>")]
    [InlineData("array </p>")]
    [InlineData("array <br/>")]
    [InlineData("array <br />")]
    public void TryConsume_HtmlAfterArray_IsNotAGenericArgument(string input)
    {
        PhpDocTypeParser.TryConsume(input, out var normalized, out var remainder)
            .Should().BeTrue();
        normalized.Should().Be("array");
        remainder.TrimStart().Should().StartWith("<");
    }

    [Theory]
    [InlineData("array<string, mixed>", "array<string, mixed>")]
    [InlineData("array<object>", "array<object>")]
    [InlineData("array<T>", "array<T>")]
    [InlineData("array <int, string>", "array<int, string>")]
    [InlineData("Collection<T>", "Collection<T>")]
    [InlineData("array<int, Foo>", "array<int, Foo>")]
    public void TryConsume_RealGenerics_AreStillConsumed(string input, string expected)
    {
        PhpDocTypeParser.TryConsume(input, out var normalized, out _)
            .Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Fact]
    public void Normalize_CompleteHtmlTaggedArray_IsNotArrayP()
    {
        // Slice leftover from a PhpStorm `@return array <p>…</p>` must not become array<p>.
        PhpDocTypeParser.Normalize("array <p>").Should().Be("mixed");
    }
}
