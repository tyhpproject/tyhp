using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Live Tyhp <c>extension { }</c> members accept optional <c>#[…]</c> the same way
/// tyhpdef standalone extension members do.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class TyhpExtensionMemberAttributesParseTests
{
    [Fact]
    public void Parse_AttributeOnBraceExtensionFunction_AttachesToFunction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension ArrayHelpers extends array {
                #[\Tyhp\Optimize\Pure]
                function sorted(): array {
                    return $this;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();

        var fn = Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "sorted").Subject;
        fn.AstAttributes.Should().HaveCount(1);
        AttributeName(fn.AstAttributes[0]).Should().Be("\\Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void Parse_AttributeOnShortExtensionFunction_AttachesToFunction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringHelpers extends string {
                #[\Tyhp\Optimize\Pure]
                fn length(): int => \strlen($this);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();

        var fn = Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "length").Subject;
        fn.AstAttributes.Should().HaveCount(1);
        AttributeName(fn.AstAttributes[0]).Should().Be("\\Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void Parse_AttributeOnExtensionOperator_AttachesToOperator()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension MoneyOperators extends int {
                #[\Tyhp\Optimize\Pure]
                operator + (self $left, self $right): int {
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();

        var op = Flatten(result.Ast).OfType<TyhpOperatorOverloadAst>().Should().ContainSingle().Subject;
        op.AstAttributes.Should().HaveCount(1);
        AttributeName(op.AstAttributes[0]).Should().Be("\\Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void Parse_ExtensionMemberWithoutAttributes_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringHelpers extends string {
                function pad(): string {
                    return $this;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();

        var fn = Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "pad").Subject;
        fn.AstAttributes.Should().BeEmpty();
    }

    private static string? AttributeName(IBase2Ast attribute)
        => (attribute as PhpAttributeAst)?.Name is PhpNameAst name ? name.ValueString : null;

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
