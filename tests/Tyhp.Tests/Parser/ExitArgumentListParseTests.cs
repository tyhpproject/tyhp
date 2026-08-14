using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class ExitArgumentListParseTests
{
    [Fact]
    public void Parse_BareExitAndDie_HaveNoArgumentListOperand()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            exit;
            die;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exits = FindExitOps(result.Ast!);
        exits.Should().HaveCount(2);
        foreach (var exit in exits)
        {
            exit.Operand.Should().BeNull();
        }

        exits.Select(OperatorText).Should().BeEquivalentTo(["exit", "die"]);
    }

    [Fact]
    public void Parse_EmptyExitAndDieCalls_HaveEmptyArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            exit();
            die();
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exits = FindExitOps(result.Ast!);
        exits.Should().HaveCount(2);
        foreach (var exit in exits)
        {
            RequireArgs(exit).Should().BeEmpty();
        }
    }

    [Fact]
    public void Parse_ExitWithPositionalArg_BuildsArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            exit(0);
            die(1);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exits = FindExitOps(result.Ast!);
        exits.Should().HaveCount(2);

        var exitArgs = RequireArgs(exits.Single(e => OperatorText(e) == "exit"));
        var dieArgs = RequireArgs(exits.Single(e => OperatorText(e) == "die"));

        exitArgs.Should().HaveCount(1);
        dieArgs.Should().HaveCount(1);
        exitArgs[0].Name.Should().BeNull();
        exitArgs[0].IsVariadic.Should().BeFalse();
        dieArgs[0].Name.Should().BeNull();
        IntLiteral(exitArgs[0].Expression).Should().Be(0);
        IntLiteral(dieArgs[0].Expression).Should().Be(1);
    }

    [Fact]
    public void Parse_ExitFirstClassCallable_BuildsVariadicEllipsisArg()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            exit(...);
            die(...);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exits = FindExitOps(result.Ast!);
        exits.Should().HaveCount(2);
        foreach (var exit in exits)
        {
            var args = RequireArgs(exit);
            args.Should().HaveCount(1);
            args[0].IsVariadic.Should().BeTrue();
            args[0].Expression.Should().BeNull();
        }
    }

    [Fact]
    public void Parse_ExitNamedArgument_BuildsNamedArgumentList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            exit(status: 0);
            die(status: 1);
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exits = FindExitOps(result.Ast!);
        exits.Should().HaveCount(2);

        var exitArgs = RequireArgs(exits.Single(e => OperatorText(e) == "exit"));
        var dieArgs = RequireArgs(exits.Single(e => OperatorText(e) == "die"));

        exitArgs.Should().HaveCount(1);
        dieArgs.Should().HaveCount(1);
        exitArgs[0].Name?.ValueString.Should().Be("status");
        dieArgs[0].Name?.ValueString.Should().Be("status");
        IntLiteral(exitArgs[0].Expression).Should().Be(0);
        IntLiteral(dieArgs[0].Expression).Should().Be(1);
    }

    private static List<PhpArgumentAst> RequireArgs(PhpUnaryOpAst exit)
    {
        exit.Operand.Should().BeOfType<PhpArgumentListAst>();
        return ((PhpArgumentListAst)exit.Operand!).GetAllNotNull().ToList();
    }

    private static string? OperatorText(PhpUnaryOpAst exit)
        => exit.Operator?.ValueString;

    private static long? IntLiteral(IExpression? expression)
        => (expression as PhpScalarAst)?.ValueInt64;

    private static List<PhpUnaryOpAst> FindExitOps(IBase2Ast root)
    {
        var exits = new List<PhpUnaryOpAst>();
        Collect(root, exits);
        return exits;
    }

    private static void Collect(IBase2Ast? node, List<PhpUnaryOpAst> exits)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpUnaryOpAst unary
            && (string.Equals(unary.Operator?.ValueString, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unary.Operator?.ValueString, "die", StringComparison.OrdinalIgnoreCase)))
        {
            exits.Add(unary);
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, exits);
        }
    }
}
