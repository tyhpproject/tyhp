using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.TestHelpers;

public static class AstAssertions
{
    public static void ShouldBeOfAstType<T>(this IBase2Ast? node) where T : Base2Ast
    {
        node.Should().NotBeNull();
        node.Should().BeOfType<T>();
    }

    public static void ShouldHaveChildCount(this IBase2Ast? node, int expectedCount)
    {
        node.Should().NotBeNull();
        node!.AstChildren.Count.Should().Be(expectedCount);
    }

    public static void ShouldHaveChildOfType<T>(this IBase2Ast? node) where T : Base2Ast
    {
        node.Should().NotBeNull();
        node!.AstChildren.Should().Contain(child => child is T);
    }

    public static void ShouldMatchStructure(Base2Ast? original, Base2Ast? roundTripped)
    {
        original.Should().NotBeNull();
        roundTripped.Should().NotBeNull();
        roundTripped!.GetType().Should().Be(original!.GetType());
        roundTripped.NodeType.Should().Be(original.NodeType);
        roundTripped.ValueString.Should().Be(original.ValueString);
        roundTripped.ValueInt64.Should().Be(original.ValueInt64);
        roundTripped.ValueDecimal.Should().Be(original.ValueDecimal);
        roundTripped.ValueBoolean.Should().Be(original.ValueBoolean);
        roundTripped.DocComment.Should().Be(original.DocComment);
        roundTripped.AstChildren.Count.Should().Be(original.AstChildren.Count);

        for (var i = 0; i < original.AstChildren.Count; i++)
        {
            var left = original.AstChildren[i] as Base2Ast;
            var right = roundTripped.AstChildren[i] as Base2Ast;
            if (left == null || right == null)
            {
                original.AstChildren[i].Should().Be(roundTripped.AstChildren[i]);
                continue;
            }

            ShouldMatchStructure(left, right);
        }
    }
}
