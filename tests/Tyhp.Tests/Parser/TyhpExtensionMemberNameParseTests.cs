using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Live <c>extension { }</c> members use method-style names
/// (<c>identifierWithoutConstructor</c> / <c>semiReserved</c>), so PHP keywords
/// that are legal as methods — including <c>match</c>, <c>and</c>, and <c>or</c> —
/// parse. Free functions keep <c>functionName</c> and still reject those tokens.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class TyhpExtensionMemberNameParseTests
{
    [Theory]
    [InlineData("match")]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("list")]
    [InlineData("empty")]
    public void Parse_BraceExtensionFunction_AllowsMethodStyleName(string name)
    {
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\n" +
            "extension StringHelpers extends string {\n" +
            "    function " + name + "(): string {\n" +
            "        return $this;\n" +
            "    }\n" +
            "}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == name);
    }

    [Theory]
    [InlineData("match")]
    [InlineData("and")]
    [InlineData("or")]
    public void Parse_ShortExtensionFunction_AllowsMethodStyleName(string name)
    {
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\n" +
            "extension BoolHelpers extends bool {\n" +
            "    fn " + name + "(): bool => $this;\n" +
            "}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var fn = Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == name).Subject;
        fn.IsShortSyntax.Should().BeTrue();
    }

    [Fact]
    public void Parse_GenericBraceExtensionFunctionNamedMatch_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringHelpers extends string {
                function match<T>(T $pattern): ?array {
                    return null;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "match");
    }

    [Theory]
    [InlineData("match")]
    [InlineData("and")]
    [InlineData("or")]
    public void Parse_FreeFunction_StillRejectsReservedName(string name)
    {
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\nfunction " + name + "(): void {}\n");

        result.Diagnostics.HasErrors.Should().BeTrue(
            $"userland `function {name}` must not parse (reserved keyword; functionName is T_STRING only)");
    }

    private static List<IBase2Ast> Flatten(IBase2Ast? root)
    {
        var result = new List<IBase2Ast>();
        if (root is null)
        {
            return result;
        }

        var stack = new Stack<IBase2Ast>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            result.Add(node);
            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }
}
