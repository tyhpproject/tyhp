using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parse + AST coverage for PHP 8.5 call-shaped <c>clone(...)</c> vs unary
/// <c>clone $x</c> / parenthesized <c>clone($x)</c> (php-src <c>clone_argument_list</c>).
/// Binder / tyhpdef / emit are out of scope here.
/// </summary>
[Trait("Category", "Parser")]
public class CloneArgumentListParseTests
{
    [Fact]
    public void Parse_UnaryClone_HasExpressionOperand()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone $obj;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var clone = FindClones(result.Ast!).Should().ContainSingle().Subject;
        clone.Operand.Should().NotBeOfType<PhpArgumentListAst>();
        VariableName(clone.Operand).Should().Be("$obj");
    }

    [Theory]
    [InlineData("clone($obj)")]
    [InlineData("clone ($obj)")]
    public void Parse_ParenthesizedSingleExpr_RemainsUnaryNotCallList(string expression)
    {
        var result = ParserTestHelper.ParseTyhpContent($"""
            <?tyhp
            $a = {expression};
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var clone = FindClones(result.Ast!).Should().ContainSingle().Subject;
        clone.Operand.Should().NotBeOfType<PhpArgumentListAst>(
            "`clone($obj)` must stay unary + parenthesized expr per php-src ambiguity rules");
        VariableName(UnwrapParenthesized(clone.Operand)).Should().Be("$obj");
    }

    [Fact]
    public void Parse_EmptyCloneCall_BuildsEmptyArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone();
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject).Should().BeEmpty();
    }

    [Fact]
    public void Parse_TrailingCommaSingleArg_BuildsCallArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone($obj,);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var args = RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject);
        args.Should().HaveCount(1);
        args[0].Name.Should().BeNull();
        args[0].IsVariadic.Should().BeFalse();
        VariableName(args[0].Expression).Should().Be("$obj");
    }

    [Fact]
    public void Parse_MultiArgClone_BuildsCallArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone($obj, $props);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var args = RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject);
        args.Should().HaveCount(2);
        VariableName(args[0].Expression).Should().Be("$obj");
        VariableName(args[1].Expression).Should().Be("$props");
    }

    [Fact]
    public void Parse_MultiArgTrailingComma_BuildsCallArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone($obj, $props,);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var args = RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject);
        args.Should().HaveCount(2);
        VariableName(args[0].Expression).Should().Be("$obj");
        VariableName(args[1].Expression).Should().Be("$props");
    }

    /// <summary>
    /// php-src <c>clone_argument_list</c> ambiguity: bare <c>clone($x)</c> is unary +
    /// parenthesized expr; a trailing comma or second arg forces the call production.
    /// </summary>
    [Fact]
    public void Parse_Ambiguity_ParenthesizedSingleIsUnary_CommaFormsAreCalls()
    {
        var unaryResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone($x);
            """);
        var trailingCommaResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $b = clone($x,);
            """);
        var multiArgResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $c = clone($x, $y);
            """);

        foreach (var result in new[] { unaryResult, trailingCommaResult, multiArgResult })
        {
            result.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        }

        var unary = FindClones(unaryResult.Ast!).Should().ContainSingle().Subject;
        unary.Operand.Should().NotBeOfType<PhpArgumentListAst>(
            "`clone($x)` must remain unary (parenthesized operand), not a call list");
        VariableName(UnwrapParenthesized(unary.Operand)).Should().Be("$x");

        var trailing = RequireArgs(FindClones(trailingCommaResult.Ast!).Should().ContainSingle().Subject);
        trailing.Should().HaveCount(1);
        VariableName(trailing[0].Expression).Should().Be("$x");

        var multi = RequireArgs(FindClones(multiArgResult.Ast!).Should().ContainSingle().Subject);
        multi.Should().HaveCount(2);
        VariableName(multi[0].Expression).Should().Be("$x");
        VariableName(multi[1].Expression).Should().Be("$y");
    }

    [Fact]
    public void Parse_CloneFirstClassCallable_BuildsVariadicEllipsisArg()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone(...);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var args = RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject);
        args.Should().HaveCount(1);
        args[0].IsVariadic.Should().BeTrue();
        args[0].Expression.Should().BeNull();
    }

    [Fact]
    public void Parse_NamedFirstArg_BuildsCallArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone(object: $obj);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var args = RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject);
        args.Should().HaveCount(1);
        args[0].Name?.ValueString.Should().Be("object");
        VariableName(args[0].Expression).Should().Be("$obj");
    }

    [Fact]
    public void Parse_UnpackFirstArg_BuildsCallArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = clone(...$objs);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var args = RequireArgs(FindClones(result.Ast!).Should().ContainSingle().Subject);
        args.Should().HaveCount(1);
        args[0].IsVariadic.Should().BeTrue();
        VariableName(args[0].Expression).Should().Be("$objs");
    }

    [Fact]
    public void Parse_FixtureFile_AcceptsAllCloneForms()
    {
        var result = ParserTestHelper.ParseFile(
            Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/parser/clone_argument_list.tyhp"));

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var clones = FindClones(result.Ast!);
        clones.Should().HaveCount(10);

        // First three: unary (bare + two parenthesized)
        clones.Take(3).Should().OnlyContain(c => !(c.Operand is PhpArgumentListAst));

        // Remaining seven: call-shaped
        clones.Skip(3).Should().OnlyContain(c => c.Operand is PhpArgumentListAst);
    }

    private static List<PhpArgumentAst> RequireArgs(PhpUnaryOpAst clone)
    {
        clone.Operand.Should().BeOfType<PhpArgumentListAst>();
        return ((PhpArgumentListAst)clone.Operand!).GetAllNotNull().ToList();
    }

    private static string? VariableName(IExpression? expression)
    {
        return expression switch
        {
            PhpVariableAst variable => variable.VariableToken?.ValueString,
            PhpDereferenceableExpressionAst paren => VariableName(paren.Expression),
            PhpDereferenceableAst dref => VariableName(dref.Base as IExpression),
            _ => null,
        };
    }

    private static IExpression? UnwrapParenthesized(IExpression? expression)
        => expression is PhpDereferenceableExpressionAst paren
            ? paren.Expression
            : expression;

    private static List<PhpUnaryOpAst> FindClones(IBase2Ast root)
    {
        var clones = new List<PhpUnaryOpAst>();
        Collect(root, clones);
        return clones;
    }

    private static void Collect(IBase2Ast? node, List<PhpUnaryOpAst> clones)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpUnaryOpAst unary
            && string.Equals(unary.Operator?.ValueString, "clone", StringComparison.OrdinalIgnoreCase))
        {
            clones.Add(unary);
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, clones);
        }
    }
}
