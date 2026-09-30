using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parse + AST coverage for <c>yield $value</c> / <c>yield $key => $value</c>
/// (<c>PhpYieldAst</c>) vs unary <c>yield from</c> and bare <c>yield;</c>.
/// Checker and emit are out of scope here.
/// </summary>
[Trait("Category", "Parser")]
public class YieldExpressionParseTests
{
    [Fact]
    public void Parse_YieldValue_BuildsPhpYieldAstWithValueOnly()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                yield $value;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var yield = FindYields(result.Ast!).Should().ContainSingle().Subject;
        yield.KeyExpr.Should().BeNull();
        VariableName(yield.ValueExpr).Should().Be("$value");
    }

    [Fact]
    public void Parse_YieldKeyValue_PreservesKeyAndValue()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                yield $key => $value;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var yield = FindYields(result.Ast!).Should().ContainSingle().Subject;
        VariableName(yield.KeyExpr).Should().Be("$key");
        VariableName(yield.ValueExpr).Should().Be("$value");
    }

    [Fact]
    public void Parse_YieldKeyValue_LiteralKeyIsNotDropped()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                yield 2 => $value;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var yield = FindYields(result.Ast!).Should().ContainSingle().Subject;
        (yield.KeyExpr as PhpScalarAst)?.ValueInt64.Should().Be(2);
        VariableName(yield.ValueExpr).Should().Be("$value");
    }

    [Fact]
    public void Parse_ValuelessYield_InExpressionPosition_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                $x = yield;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        FindYields(result.Ast!).Should().BeEmpty();
        var unary = FindBareYield(result.Ast!).Should().ContainSingle().Subject;
        unary.Operand.Should().BeNull();
    }

    [Fact]
    public void Parse_YieldKeyValue_InExpressionPosition_PreservesKey()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                $sent = yield $key => $value;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var yield = FindYields(result.Ast!).Should().ContainSingle().Subject;
        VariableName(yield.KeyExpr).Should().Be("$key");
        VariableName(yield.ValueExpr).Should().Be("$value");
    }

    [Fact]
    public void Parse_YieldFrom_RemainsUnaryNotPhpYieldAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                yield from $inner;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        FindYields(result.Ast!).Should().BeEmpty();
        var unary = FindYieldFrom(result.Ast!).Should().ContainSingle().Subject;
        VariableName(unary.Operand).Should().Be("$inner");
    }

    [Fact]
    public void Parse_BareYield_RemainsUnaryNotPhpYieldAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                yield;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        FindYields(result.Ast!).Should().BeEmpty();
        var unary = FindBareYield(result.Ast!).Should().ContainSingle().Subject;
        unary.Operand.Should().BeNull();
    }

    [Fact]
    public void Parse_MixedYieldForms_KeepDistinctShapes()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function gen(): \Generator {
                yield $a;
                yield $k => $v;
                yield from $inner;
                yield;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var yields = FindYields(result.Ast!);
        yields.Should().HaveCount(2);
        yields[0].KeyExpr.Should().BeNull();
        VariableName(yields[0].ValueExpr).Should().Be("$a");
        VariableName(yields[1].KeyExpr).Should().Be("$k");
        VariableName(yields[1].ValueExpr).Should().Be("$v");

        FindYieldFrom(result.Ast!).Should().ContainSingle();
        FindBareYield(result.Ast!).Should().ContainSingle();
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

    private static List<PhpYieldAst> FindYields(IBase2Ast root)
    {
        var yields = new List<PhpYieldAst>();
        Collect(root, yields);
        return yields;
    }

    private static void Collect(IBase2Ast? node, List<PhpYieldAst> yields)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpYieldAst yield)
        {
            yields.Add(yield);
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, yields);
        }
    }

    private static List<PhpUnaryOpAst> FindYieldFrom(IBase2Ast root)
    {
        var found = new List<PhpUnaryOpAst>();
        CollectUnaryYield(root, found, yieldFrom: true);
        return found;
    }

    private static List<PhpUnaryOpAst> FindBareYield(IBase2Ast root)
    {
        var found = new List<PhpUnaryOpAst>();
        CollectUnaryYield(root, found, yieldFrom: false);
        return found;
    }

    private static void CollectUnaryYield(IBase2Ast? node, List<PhpUnaryOpAst> found, bool yieldFrom)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpUnaryOpAst unary)
        {
            var op = unary.Operator?.ValueString ?? "";
            var isYieldFrom = op.Contains("yield", StringComparison.OrdinalIgnoreCase)
                && op.Contains("from", StringComparison.OrdinalIgnoreCase);
            var isBareYield = string.Equals(op, "yield", StringComparison.OrdinalIgnoreCase)
                && unary.Operand is null;
            if (yieldFrom ? isYieldFrom : isBareYield)
            {
                found.Add(unary);
            }
        }

        foreach (var child in node.AstChildren)
        {
            CollectUnaryYield(child, found, yieldFrom);
        }
    }
}
