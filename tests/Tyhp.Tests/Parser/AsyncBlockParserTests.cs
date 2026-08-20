using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class AsyncBlockParserTests
{
    [Fact]
    public void Parse_AsyncBlock_ProducesTyhpAsyncBlockAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(): mixed {
                return async {
                    return 1;
                };
            }
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
        Flatten(result.Ast).OfType<TyhpAsyncBlockAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<PhpInlineFunctionAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyAsyncBlock_ProducesTyhpAsyncBlockAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(): mixed {
                return async {};
            }
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
        Flatten(result.Ast).OfType<TyhpAsyncBlockAst>().Should().ContainSingle();
    }

    [Fact]
    public void Parse_AsyncFunctionClosure_IsStillInlineFunction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(): mixed {
                return async function () {
                    return 1;
                };
            }
            """);

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", result.Diagnostics)}");
        Flatten(result.Ast).OfType<PhpInlineFunctionAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<TyhpAsyncBlockAst>().Should().BeEmpty();
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
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
