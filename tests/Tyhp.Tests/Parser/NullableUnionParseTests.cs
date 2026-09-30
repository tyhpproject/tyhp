using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Parser;

/// <summary>
/// <c>?(A|B)</c> and <c>?(A&amp;B)</c> are one nullable union or intersection.
/// <c>?T</c> and <c>A|B</c> stay the existing single-type and union spellings.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class NullableUnionParseTests
{
    [Theory]
    [InlineData("tyhp-alias")]
    [InlineData("tyhp-parameter")]
    [InlineData("tyhp-return")]
    [InlineData("tyhpdef-parameter")]
    [InlineData("tyhpdef-return")]
    public void Parse_NullableParenthesizedUnion_IsNullableUnion(string position)
    {
        var result = Parse(position, "?(string|int)");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`?(string|int)` in {position} should parse: {Describe(result)}");
        var typeExpr = FindType(result, position);
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Union, DescribeType(typeExpr));
        typeExpr.IsNullable.Should().BeTrue(DescribeType(typeExpr));
        typeExpr.Types!.GetAllNotNull().OfType<PhpTypeExpressionAst>().Should().BeEmpty();
        MemberNames(typeExpr).Should().Equal("string", "int");
    }

    [Theory]
    [InlineData("tyhp-alias")]
    [InlineData("tyhp-return")]
    public void Parse_NullableParenthesizedIntersection_IsNullableIntersection(string position)
    {
        var result = Parse(position, @"?(\Foo&\Bar)");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`?(Foo&Bar)` in {position} should parse: {Describe(result)}");
        var typeExpr = FindType(result, position);
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Intersection, DescribeType(typeExpr));
        typeExpr.IsNullable.Should().BeTrue(DescribeType(typeExpr));
        typeExpr.Types!.GetAllNotNull().Should().AllBeOfType<PhpNamedTypeAst>();
        MemberNames(typeExpr).Should().Equal(@"\Foo", @"\Bar");
    }

    [Theory]
    [InlineData("tyhp-alias")]
    [InlineData("tyhp-parameter")]
    [InlineData("tyhp-return")]
    public void Parse_NullableSingleType_StaysSimpleNullable(string position)
    {
        var result = Parse(position, "?string");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`?string` in {position} should parse: {Describe(result)}");
        var typeExpr = FindType(result, position);
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Simple, DescribeType(typeExpr));
        typeExpr.IsNullable.Should().BeTrue();
        MemberNames(typeExpr).Should().Equal("string");
    }

    [Theory]
    [InlineData("tyhp-alias")]
    [InlineData("tyhp-parameter")]
    [InlineData("tyhp-return")]
    public void Parse_Union_StaysNonNullableUnion(string position)
    {
        var result = Parse(position, "string|int");

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`string|int` in {position} should parse: {Describe(result)}");
        var typeExpr = FindType(result, position);
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Union, DescribeType(typeExpr));
        typeExpr.IsNullable.Should().BeFalse();
        MemberNames(typeExpr).Should().Equal("string", "int");
    }

    private static ParseResult Parse(string position, string type)
    {
        var source = position switch
        {
            "tyhp-alias" => $"""
                <?tyhp
                type N = {type};
                """,
            "tyhp-parameter" => "<?tyhp\nfunction f(" + type + " $x): void {}\n",
            "tyhp-return" => "<?tyhp\nfunction f(): " + type + " { return null; }\n",
            "tyhpdef-parameter" => $"""
                <?tyhpdef
                function f({type} $x): void;
                """,
            "tyhpdef-return" => $"""
                <?tyhpdef
                function f(): {type};
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };

        return position.StartsWith("tyhpdef", StringComparison.Ordinal)
            ? ParserTestHelper.ParseTyhpdefContent(source)
            : ParserTestHelper.ParseTyhpContent(source);
    }

    private static PhpTypeExpressionAst FindType(ParseResult result, string position)
    {
        result.Ast.Should().NotBeNull();
        ITypeExpression? type = position switch
        {
            "tyhp-alias" => Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Should().ContainSingle().Subject.TypeExpression,
            "tyhp-parameter" or "tyhpdef-parameter" => Flatten(result.Ast).OfType<PhpParameterAst>()
                .Should().ContainSingle().Subject.Type,
            "tyhp-return" => Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
                .Should().ContainSingle().Subject.ReturnType,
            "tyhpdef-return" => Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>()
                .Should().ContainSingle().Subject.ReturnType,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };

        return type.Should().BeOfType<PhpTypeExpressionAst>().Subject;
    }

    private static IEnumerable<string> MemberNames(PhpTypeExpressionAst typeExpr)
        => typeExpr.Types?.GetAllNotNull().Select(Spell) ?? [];

    private static string Spell(ITypeExpression type) => type switch
    {
        PhpBuiltinTypeAst builtin => builtin.Identifier ?? "",
        PhpNamedTypeAst named when named.Name is PhpNameAst name => name.ValueString ?? "",
        PhpTypeExpressionAst composite =>
            string.Join(",", composite.Types?.GetAllNotNull().Select(Spell) ?? []),
        _ => type.ToString() ?? "",
    };

    private static string DescribeType(PhpTypeExpressionAst typeExpr)
        => $"{typeExpr.TypeKind} nullable={typeExpr.IsNullable} members=[{string.Join(", ", MemberNames(typeExpr))}]";

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IEnumerable<IBase2Ast> Flatten(IBase2Ast? node)
    {
        if (node is null)
        {
            yield break;
        }

        yield return node;
        foreach (var child in node.AstChildren)
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }
}
