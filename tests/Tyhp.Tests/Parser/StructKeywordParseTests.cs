using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class StructKeywordParseTests
{
    [Fact]
    public void Parse_ClassNamedStruct_StillParsesAsOrdinaryClass()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class struct { public function bar(): void {} }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "struct");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_ClassNamedStruct_FollowedByExtends_StillParsesAsOrdinaryClass()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Base { public function bar(): void {} }
            class struct extends Base { public function bar(): void {} }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().Contain(c => c.Identifier == "struct");
    }

    [Fact]
    public void Parse_FunctionNamedStruct_StillParsesAsOrdinaryFunction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function struct(): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "struct");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_NewStructCall_StillParsesAsClassConstruction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class struct {}
            $x = new struct();
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "struct");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
        var constructed = Flatten(result.Ast).OfType<PhpNewAst>().Should().ContainSingle().Subject;
        constructed.ClassName.Should().NotBeNull();
        constructed.ClassName.Should().BeOfType<PhpNameAst>()
            .Which.ValueString.Should().Be("struct");
    }

    [Fact]
    public void Parse_NewAnonymousStruct_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $p = new struct { int $x; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().ContainSingle()
            .Which.IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public void Parse_NewAnonymousStruct_WithEmptyParens_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $p = new struct() { int $x; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().ContainSingle()
            .Which.IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public void Parse_StructShapeAlias_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Point = struct { int $x; int $y; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Point");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_InlineStructShapeParameter_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(struct { int $n; } $s): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "f");
    }

    [Fact]
    public void Parse_StructExtendsShape_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Point = struct { int $x; };
            type Named = struct extends Point { string $name; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().HaveCount(2);
    }

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

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
