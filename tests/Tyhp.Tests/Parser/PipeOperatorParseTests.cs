using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parse + AST coverage for PHP 8.5 pipe <c>|&gt;</c> (<c>T_PIPE</c>).
/// Checker typing and emitter lowering are out of scope here.
/// </summary>
[Trait("Category", "Parser")]
public class PipeOperatorParseTests
{
    [Fact]
    public void Parse_SinglePipe_BuildsBinaryOpAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var pipe = FindPipes(result.Ast!).Should().ContainSingle().Subject;
        pipe.Operator!.ValueString.Should().Be("|>");
        pipe.Operator.ValueInt64.Should().Be(TyhpParser.T_PIPE);
        PhpBinaryOperatorExtensions.FromToken(TyhpParser.T_PIPE).Should().Be(PhpBinaryOperator.Pipe);
        VariableName(pipe.Left).Should().Be("$x");
        VariableName(pipe.Right).Should().Be("$f");
    }

    [Fact]
    public void Parse_PipeChain_IsLeftAssociative()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f |> $g;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x |> $f) |> $g
        var outer = FindPipes(result.Ast!).Should().ContainSingle(p => VariableName(p.Right) == "$g").Subject;
        outer.Left.Should().BeOfType<PhpBinaryOpAst>();
        var inner = (PhpBinaryOpAst)outer.Left!;
        IsPipe(inner).Should().BeTrue();
        VariableName(inner.Left).Should().Be("$x");
        VariableName(inner.Right).Should().Be("$f");
        VariableName(outer.Right).Should().Be("$g");
    }

    [Fact]
    public void Parse_PipeTripleChain_IsLeftAssociative()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f |> $g |> $h;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // (($x |> $f) |> $g) |> $h
        var outer = FindPipes(result.Ast!).Should().ContainSingle(p => VariableName(p.Right) == "$h").Subject;
        outer.Left.Should().BeOfType<PhpBinaryOpAst>();
        var mid = (PhpBinaryOpAst)outer.Left!;
        IsPipe(mid).Should().BeTrue();
        VariableName(mid.Right).Should().Be("$g");
        mid.Left.Should().BeOfType<PhpBinaryOpAst>();
        var inner = (PhpBinaryOpAst)mid.Left!;
        IsPipe(inner).Should().BeTrue();
        VariableName(inner.Left).Should().Be("$x");
        VariableName(inner.Right).Should().Be("$f");
    }

    [Fact]
    public void Parse_Pipe_BindsAfterAddition()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = 5 + 2 |> $f;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // (5 + 2) |> $f
        var pipe = FindPipes(result.Ast!).Should().ContainSingle().Subject;
        pipe.Left.Should().BeOfType<PhpBinaryOpAst>();
        var add = (PhpBinaryOpAst)pipe.Left!;
        add.Operator!.ValueString.Should().Be("+");
        VariableName(pipe.Right).Should().Be("$f");
    }

    [Fact]
    public void Parse_Pipe_BindsAfterConcat()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x . $y |> $f;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x . $y) |> $f — pipe sits after concat in php-src precedence
        var pipe = FindPipes(result.Ast!).Should().ContainSingle().Subject;
        pipe.Left.Should().BeOfType<PhpBinaryOpAst>();
        var concat = (PhpBinaryOpAst)pipe.Left!;
        concat.Operator!.ValueString.Should().Be(".");
        VariableName(concat.Left).Should().Be("$x");
        VariableName(concat.Right).Should().Be("$y");
        VariableName(pipe.Right).Should().Be("$f");
    }

    [Fact]
    public void Parse_Pipe_BindsBeforeComparison()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f < 4;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x |> $f) < 4 — pipe sits before comparison size ops
        var assign = FindTopAssignments(result.Ast!).Should().ContainSingle().Subject;
        assign.Right.Should().BeOfType<PhpBinaryOpAst>();
        var cmp = (PhpBinaryOpAst)assign.Right!;
        cmp.Operator!.ValueString.Should().Be("<");
        cmp.Left.Should().BeOfType<PhpBinaryOpAst>();
        IsPipe((PhpBinaryOpAst)cmp.Left!).Should().BeTrue();
        (cmp.Right as PhpScalarAst)?.ValueInt64.Should().Be(4);
    }

    [Fact]
    public void Parse_Pipe_BindsBeforeEquality()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f == 4;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x |> $f) == 4
        var assign = FindTopAssignments(result.Ast!).Should().ContainSingle().Subject;
        assign.Right.Should().BeOfType<PhpBinaryOpAst>();
        var eq = (PhpBinaryOpAst)assign.Right!;
        eq.Operator!.ValueString.Should().Be("==");
        eq.Left.Should().BeOfType<PhpBinaryOpAst>();
        IsPipe((PhpBinaryOpAst)eq.Left!).Should().BeTrue();
        (eq.Right as PhpScalarAst)?.ValueInt64.Should().Be(4);
    }

    [Fact]
    public void Parse_Pipe_BindsBeforeBooleanAnd()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f && $y;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x |> $f) && $y
        var assign = FindTopAssignments(result.Ast!).Should().ContainSingle().Subject;
        assign.Right.Should().BeOfType<PhpBinaryOpAst>();
        var and = (PhpBinaryOpAst)assign.Right!;
        and.Operator!.ValueString.Should().Be("&&");
        and.Left.Should().BeOfType<PhpBinaryOpAst>();
        IsPipe((PhpBinaryOpAst)and.Left!).Should().BeTrue();
        VariableName(and.Right).Should().Be("$y");
    }

    [Fact]
    public void Parse_Pipe_BindsBeforeCoalesce()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f ?? $fallback;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x |> $f) ?? $fallback
        var assign = FindTopAssignments(result.Ast!).Should().ContainSingle().Subject;
        assign.Right.Should().BeOfType<PhpBinaryOpAst>();
        var coalesce = (PhpBinaryOpAst)assign.Right!;
        coalesce.Operator!.ValueString.Should().Be("??");
        coalesce.Left.Should().BeOfType<PhpBinaryOpAst>();
        IsPipe((PhpBinaryOpAst)coalesce.Left!).Should().BeTrue();
        VariableName(coalesce.Right).Should().Be("$fallback");
    }

    [Fact]
    public void Parse_Pipe_BindsBeforeTernary()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f ? $t : $u;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // ($x |> $f) ? $t : $u
        var assign = FindTopAssignments(result.Ast!).Should().ContainSingle().Subject;
        assign.Right.Should().BeOfType<PhpTernaryOpAst>();
        var ternary = (PhpTernaryOpAst)assign.Right!;
        ternary.Condition.Should().BeOfType<PhpBinaryOpAst>();
        IsPipe((PhpBinaryOpAst)ternary.Condition!).Should().BeTrue();
        VariableName(ternary.TrueExpr).Should().Be("$t");
        VariableName(ternary.FalseExpr).Should().Be("$u");
    }

    [Fact]
    public void Parse_Pipe_IsRhsOfAssignment()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $a = $x |> $f;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        // Assignment binds looser than pipe: $a = ($x |> $f)
        var assign = FindTopAssignments(result.Ast!).Should().ContainSingle().Subject;
        assign.Operator!.ValueString.Should().Be("=");
        VariableName(assign.Left).Should().Be("$a");
        assign.Right.Should().BeOfType<PhpBinaryOpAst>();
        IsPipe((PhpBinaryOpAst)assign.Right!).Should().BeTrue();
    }

    [Fact]
    public void Parse_PipeFixtureFile_Succeeds()
    {
        var result = ParserTestHelper.ParseFile(
            Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/parser/pipe_operator.tyhp"));

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindPipes(result.Ast!).Should().HaveCountGreaterThanOrEqualTo(8);
    }

    private static bool IsPipe(PhpBinaryOpAst binary)
        => binary.Operator?.ValueInt64 == TyhpParser.T_PIPE
            || binary.Operator?.ValueString == "|>";

    private static string? VariableName(IExpression? expression)
        => (expression as PhpVariableAst)?.VariableToken?.ValueString;

    private static List<PhpBinaryOpAst> FindPipes(IBase2Ast root)
    {
        var pipes = new List<PhpBinaryOpAst>();
        CollectPipes(root, pipes);
        return pipes;
    }

    private static void CollectPipes(IBase2Ast? node, List<PhpBinaryOpAst> pipes)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpBinaryOpAst binary && IsPipe(binary))
        {
            pipes.Add(binary);
        }

        foreach (var child in node.AstChildren)
        {
            CollectPipes(child, pipes);
        }
    }

    private static List<PhpBinaryOpAst> FindTopAssignments(IBase2Ast root)
    {
        var assigns = new List<PhpBinaryOpAst>();
        CollectAssignments(root, assigns);
        return assigns;
    }

    private static void CollectAssignments(IBase2Ast? node, List<PhpBinaryOpAst> assigns)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpBinaryOpAst binary && binary.Operator?.ValueString == "=")
        {
            assigns.Add(binary);
        }

        foreach (var child in node.AstChildren)
        {
            CollectAssignments(child, assigns);
        }
    }
}
