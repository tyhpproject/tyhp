using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class TypeofTypeExprParseTests
{
    [Theory]
    [InlineData("typeof(int|string)")]
    [InlineData("typeof(Optional<int>)")]
    [InlineData("typeof(?int)")]
    [InlineData("typeof(int)")]
    [InlineData("typeof(string)")]
    [InlineData("typeof(?User)")]
    public void Parse_TypeofTypeExpression_Succeeds(string call)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            type Optional<T> = T|null;
            class User {}
            function demo(): void {
                $t = {{call}};
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{call}` should parse: {Describe(result)}");
        var typeofNodes = FindTypeof(result.Ast!).ToList();
        typeofNodes.Should().NotBeEmpty(
            $"`{call}` should produce TyhpTypeofAst");
        typeofNodes.Should().OnlyContain(t => t.TypeExpression != null);
    }

    [Fact]
    public void Parse_TypeofVariable_DoesNotParse()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(int $x): void {
                $t = typeof($x);
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "typeof takes a type expression; typeof($x) is a parse error");
    }

    private static IEnumerable<TyhpTypeofAst> FindTypeof(IBase2Ast node)
    {
        if (node is TyhpTypeofAst typeofAst)
        {
            yield return typeofAst;
        }

        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindTypeof(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
