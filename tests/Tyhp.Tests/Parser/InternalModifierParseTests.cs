using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class InternalModifierParseTests
{
    [Fact]
    public void Parse_InternalClass_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            internal class Foo {}
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "Foo");
    }

    [Fact]
    public void Parse_PublicInternalClass_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            public internal class Foo {}
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "Foo");
    }

    [Theory]
    [InlineData("internal async function helper(): void {}")]
    [InlineData("async internal function helper(): void {}")]
    public void Parse_InternalAsyncFunction_EitherModifierOrder_Succeeds(string decl)
    {
        var result = ParserTestHelper.ParseTyhpContent($"""
            <?tyhp
            {decl}
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
    }

    [Fact]
    public void Parse_InternalMembersAndTopLevel_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            internal function helper(): void {}
            internal const TAG = 1;
            class Box {
                internal function secret(): void {}
                internal int $n = 0;
                internal const C = 2;
            }
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
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
}
