using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Operator-overload operands use <c>attributedParameter</c>, so <c>#[…]</c> before
/// <c>self $left</c> (and the optional right operand) parses the same way method
/// parameters do.
/// </summary>
[Trait("Category", "Parser")]
public class OperatorOverloadParameterAttributesParseTests
{
    [Fact]
    public void Parse_AttributeOnOperatorOverloadLeftParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class Attr {}

            class Box {
                operator +(#[Attr] self $left, self $right): Box {
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
        op.RightParameter.Should().NotBeNull();
        op.RightParameter!.AstAttributes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AttributesOnBothOperatorOverloadOperands_AttachesToEachParameter()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class Attr {}

            class Box {
                operator +(#[Attr] self $left, #[Attr] self $right): Box {
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.RightParameter.Should().NotBeNull();
        AttributeName(op.LeftParameter!.AstAttributes.Should().ContainSingle().Subject).Should().Be("Attr");
        AttributeName(op.RightParameter!.AstAttributes.Should().ContainSingle().Subject).Should().Be("Attr");
    }

    [Fact]
    public void Parse_OperatorOverloadWithoutParameterAttributes_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Box {
                operator +(self $left, self $right): Box {
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().BeEmpty();
        op.RightParameter.Should().NotBeNull();
        op.RightParameter!.AstAttributes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AttributeOnUnaryOperatorOverloadParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class Attr {}

            class Box {
                operator ++(#[Attr] self $value): self {
                    return $value;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
        op.RightParameter.Should().BeNull();
    }

    [Fact]
    public void Parse_TyhpdefClassOperator_AttributeOnLeftParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Box {
                operator +(#[Attr] self $left, self $right): Box;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
    }

    [Fact]
    public void Parse_TyhpExtensionOperatorOverload_AttributeOnLeftParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class Attr {}

            class Money { public int $amount = 0; }
            extension MoneyOperators extends Money {
                operator + (#[Attr] self $left, self $right): Money {
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
        op.RightParameter.Should().NotBeNull();
        op.RightParameter!.AstAttributes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TyhpdefClassBodyExtensionOperator_AttributeOnLeftParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Box {
                extension operator +(#[Attr] self $left, self $right): self => $left;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
    }

    [Fact]
    public void Parse_TyhpdefClassBodyExtensionOperatorSignatureOnly_AttributeOnLeftParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Box {
                extension operator +(#[Attr] self $left, self $right): self;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
    }

    [Fact]
    public void Parse_TyhpdefStandaloneExtensionOperator_AttributeOnLeftParameter_AttachesToParameter()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extension StringExtensions extends string {
                operator * (#[Attr] string $left, int $right): string => $left;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindOperators(result.Ast!).Should().ContainSingle().Subject;
        op.LeftParameter.Should().NotBeNull();
        op.LeftParameter!.AstAttributes.Should().HaveCount(1);
        AttributeName(op.LeftParameter.AstAttributes[0]).Should().Be("Attr");
        op.RightParameter.Should().NotBeNull();
        op.RightParameter!.AstAttributes.Should().BeEmpty();
    }

    private static string? AttributeName(IBase2Ast attribute)
        => (attribute as PhpAttributeAst)?.Name is PhpNameAst name ? name.ValueString : null;

    private static List<TyhpOperatorOverloadAst> FindOperators(IBase2Ast root)
    {
        var operators = new List<TyhpOperatorOverloadAst>();
        Collect(root, operators);
        return operators;
    }

    private static void Collect(IBase2Ast? node, List<TyhpOperatorOverloadAst> operators)
    {
        if (node == null)
        {
            return;
        }

        if (node is TyhpOperatorOverloadAst op)
        {
            operators.Add(op);
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, operators);
        }
    }
}
