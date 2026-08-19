using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
public class BaseSymbolEndPositionTests
{
    [Fact]
    public void Constructor_CopiesEndPositionFromDeclaringAstNode()
    {
        var parse = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function greet(): void {}
            """);
        parse.Success.Should().BeTrue(because: string.Join("; ", parse.Diagnostics.Select(d => d.Message)));

        var fn = FindFunction(parse.Ast!, "greet");
        fn.Should().NotBeNull();
        fn!.EndLine.Should().BeGreaterThan(0);
        fn.EndColumn.Should().BeGreaterThan(fn.Column);

        var symbol = new FunctionDeclarationSymbol(fn.Identifier, fn, "test.tyhp");
        symbol.Line.Should().Be(fn.Line);
        symbol.Column.Should().Be(fn.Column);
        symbol.EndLine.Should().Be(fn.EndLine);
        symbol.EndColumn.Should().Be(fn.EndColumn);
    }

    [Fact]
    public void Constructor_WithoutDeclaringNode_DefaultsEndPositionToZero()
    {
        var symbol = new FunctionDeclarationSymbol("orphan");
        symbol.Line.Should().Be(0);
        symbol.Column.Should().Be(0);
        symbol.EndLine.Should().Be(0);
        symbol.EndColumn.Should().Be(0);
    }

    private static PhpFunctionDeclAst? FindFunction(IBase2Ast root, string name)
    {
        foreach (var node in Walk(root))
        {
            if (node is PhpFunctionDeclAst fn && fn.Identifier == name)
            {
                return fn;
            }
        }

        return null;
    }

    private static IEnumerable<IBase2Ast> Walk(IBase2Ast node)
    {
        yield return node;
        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
