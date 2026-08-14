using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Regression tests for `new X&lt;T&gt;(args)`. The generic argument list is ambiguous with the
/// comparison chain `(new X) &lt; T &gt; (args)`, and the argument-less `newNonDereferenceable`
/// alternative used to win that contest: the parser consumed `new X&lt;T&gt;` and then reported
/// TYHP1002 on the opening parenthesis. Only forms where the ambiguity collapsed on its own
/// (empty parens, first-class callable syntax) used to parse.
/// </summary>
[Trait("Category", "Parser")]
public class GenericNewExpressionParseTests
{
    [Theory]
    [InlineData("new Box<int>(5)")]
    [InlineData("new Box<int>()")]
    [InlineData("new Box<int>(...)")]
    [InlineData("new Box(5)")]
    [InlineData("new Pair<int, string>(5, 'x')")]
    [InlineData("new Box<Box<int>>(new Box<int>(1))")]
    [InlineData("new Box<\\Test\\Box>(null)")]
    public void Parse_GenericInstantiation_Succeeds(string expression)
    {
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\nnamespace Test;\n$x = {expression};\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{expression}` should parse: {Describe(result)}");
        result.Ast.Should().NotBeNull();
    }

    [Theory]
    [InlineData("static")]
    [InlineData("self")]
    public void Parse_GenericInstantiation_OnSelfAndStatic_Succeeds(string className)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            namespace Test;
            class Box<T> {
                public function __construct(?T $v = null): void {}
                public static function make<TX>(?TX $v = null): static {
                    return new {{className}}<TX>($v);
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`new {className}<TX>(...)` should parse: {Describe(result)}");
    }

    /// <summary>
    /// The lookahead that disambiguates generic instantiation only recognizes
    /// `new NAME [&lt;...&gt;] (`, so every other shape of `new` and every relational
    /// expression must keep parsing exactly as before.
    /// </summary>
    [Theory]
    [InlineData("$x = new Legacy(5);")]
    [InlineData("$x = new Legacy;")]
    [InlineData("$x = new $cls(5);")]
    [InlineData("$x = new ($factory)(5);")]
    [InlineData("$x = $a < $b;")]
    [InlineData("$x = ($a < $b) > $c;")]
    public void Parse_NonGenericNewAndComparison_Unaffected(string statement)
    {
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n{statement}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{statement}` should parse: {Describe(result)}");
    }

    private static string Describe(ParseResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
