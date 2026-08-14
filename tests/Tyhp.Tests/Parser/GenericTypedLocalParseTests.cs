using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Regression tests for <c>Type&lt;Arg&gt; $var = ...</c> (FOUND_BUGS #38). That shape is
/// ambiguous with the comparison chain <c>(Type &lt; Arg) &gt; $var</c>; without lookahead,
/// statement prediction prefers the comparison and emit produces invalid PHP.
/// </summary>
[Trait("Category", "Parser")]
public class GenericTypedLocalParseTests
{
    [Theory]
    [InlineData("Bag<int> $bag = new Bag<int>(5);")]
    [InlineData("Bag<T> $bag = new Bag();")]
    [InlineData("Deferred<void> $deferred = new Deferred<void>();")]
    [InlineData("array<int> $items = [];")]
    [InlineData("array<string, int> $map = [];")]
    [InlineData("?Bag<int> $bag = null;")]
    [InlineData("(Bag<int>) $bag = new Bag<int>(5);")]
    [InlineData("\\Test\\Bag<int> $bag = new \\Test\\Bag<int>(5);")]
    public void Parse_GenericTypedLocal_Succeeds(string statement)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            namespace Test;
            class Bag<T> {
                public function __construct(?T $v = null): void {}
            }
            class Deferred<T> {
                public function __construct(): void {}
            }
            function demo(): void {
                {{statement}}
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{statement}` should parse: {Describe(result)}");
        result.Ast.Should().NotBeNull();
        FindTypedLocals(result.Ast!).Should().NotBeEmpty(
            $"`{statement}` should produce a TyhpTypedVarExprAst");
    }

    [Fact]
    public void Parse_GenericTypedLocal_InForInit_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace Test;
            class Bag<T> {
                public function __construct(?T $v = null): void {}
            }
            function demo(): void {
                for (Bag<int> $i = new Bag<int>(0); false; ) {}
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"for-init generic typed local should parse: {Describe(result)}");
        FindTypedLocals(result.Ast!).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("$x = $a < $b;")]
    [InlineData("$x = ($a < $b) > $c;")]
    [InlineData("$x = $a < $b > $c;")]
    [InlineData("int $n = 1;")]
    [InlineData("$plain = 1;")]
    public void Parse_ComparisonAndNonGenericLocal_Unaffected(string statement)
    {
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n{statement}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{statement}` should parse: {Describe(result)}");
    }

    [Fact]
    public void Parse_ComparisonChain_DoesNotProduceTypedLocal()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $x = $a < $b > $c;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        FindTypedLocals(result.Ast!).Should().BeEmpty(
            "a real comparison chain must not be rewritten as a typed local");
    }

    [Theory]
    [InlineData("int|Bag<int> $x = new Bag<int>(1);")]
    [InlineData("Bag<int>|int $x = new Bag<int>(1);")]
    [InlineData("int|Bag<int>|string $x = new Bag<int>(1);")]
    public void Parse_UnionLeadingGenericTypedLocal_Succeeds(string statement)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            namespace Test;
            class Bag<T> {
                public function __construct(?T $v = null): void {}
            }
            function demo(): void {
                {{statement}}
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{statement}` should parse: {Describe(result)}");
        result.Ast.Should().NotBeNull();
        FindTypedLocals(result.Ast!).Should().NotBeEmpty(
            $"`{statement}` should produce a TyhpTypedVarExprAst");
    }

    private static IEnumerable<TyhpTypedVarExprAst> FindTypedLocals(IBase2Ast node)
    {
        if (node is TyhpTypedVarExprAst typed)
        {
            yield return typed;
        }

        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var match in FindTypedLocals(child))
            {
                yield return match;
            }
        }
    }

    private static string Describe(ParseResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
