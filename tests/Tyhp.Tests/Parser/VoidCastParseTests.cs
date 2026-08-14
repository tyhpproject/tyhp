using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parse + AST coverage for PHP 8.5 <c>(void)</c> cast — statement discard and
/// for-loop expr-list forms (php-src). Not a value-producing unary in expr position.
/// Checker typing and emitter lowering are out of scope here.
/// </summary>
[Trait("Category", "Parser")]
public class VoidCastParseTests
{
    [Fact]
    public void Parse_VoidCastStatement_BuildsUnaryCastAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            (void)$x;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var voidCast = FindVoidCasts(result.Ast!).Should().ContainSingle().Subject;
        voidCast.IsPrefix.Should().BeTrue();
        voidCast.Operator!.ValueInt64.Should().Be(TyhpParser.T_VOID_CAST);
        voidCast.Operator.ValueString.Should().Be("(void)");
        VariableName(voidCast.Operand).Should().Be("$x");
    }

    [Theory]
    [InlineData("( void )")]
    [InlineData("(VOID)")]
    [InlineData("(Void)")]
    public void Parse_VoidCastStatement_AcceptsWhitespaceAndCaseVariants(string castText)
    {
        var result = ParserTestHelper.ParseTyhpContent($"""
            <?tyhp
            {castText} $x;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var voidCast = FindVoidCasts(result.Ast!).Should().ContainSingle().Subject;
        voidCast.Operator!.ValueInt64.Should().Be(TyhpParser.T_VOID_CAST);
        voidCast.Operator.ValueString.Should().Be(castText);
        VariableName(voidCast.Operand).Should().Be("$x");
    }

    [Fact]
    public void Parse_VoidCastInValuePosition_IsRejected()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $y = (void)$x;
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "`$y = (void)$x` must not parse — (void) is not a value-producing expression");
    }

    [Fact]
    public void Parse_VoidCastInForInitAndUpdate_BuildsUnaryCastAsts()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            for ((void)$a; $i < 10; (void)$i++) {
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var loop = FindForLoops(result.Ast!).Should().ContainSingle().Subject;
        var initCast = FindVoidCastsIn(loop.InitExpressions).Should().ContainSingle().Subject;
        VariableName(initCast.Operand).Should().Be("$a");

        FindVoidCastsIn(loop.TestExpressions).Should().BeEmpty();

        var updateCast = FindVoidCastsIn(loop.UpdateExpressions).Should().ContainSingle().Subject;
        updateCast.Operator!.ValueInt64.Should().Be(TyhpParser.T_VOID_CAST);
        updateCast.Operand.Should().BeOfType<PhpUnaryOpAst>(); // $i++
    }

    [Fact]
    public void Parse_VoidCastSoleForCondition_IsRejected()
    {
        // php-src for_cond_exprs: sole item must be a plain expr — not T_VOID_CAST expr
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            for (; (void)$a; ) {
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "`for (; (void)$a; )` must not parse — void cast cannot be the sole for-condition");
    }

    [Fact]
    public void Parse_VoidCastNonFinalInForCondition_Succeeds()
    {
        // php-src: non_empty_for_exprs ',' expr — void cast allowed before a trailing plain expr
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            for (; (void)$side, $cond; ) {
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var loop = FindForLoops(result.Ast!).Should().ContainSingle().Subject;
        var condItems = loop.TestExpressions!.AstChildren.OfType<IExpression>().ToList();
        condItems.Should().HaveCount(2);
        condItems[0].Should().BeOfType<PhpUnaryOpAst>();
        ((PhpUnaryOpAst)condItems[0]).Operator!.ValueInt64.Should().Be(TyhpParser.T_VOID_CAST);
        VariableName(condItems[1]).Should().Be("$cond");
    }

    [Fact]
    public void Parse_IntCastStatement_StillWorks_AndIsNotVoid()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            (int)$x;
            (void)$y;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        FindVoidCasts(result.Ast!).Should().ContainSingle();
        FindUnaryCasts(result.Ast!, TyhpParser.T_INT_CAST).Should().ContainSingle();
    }

    [Fact]
    public void Parse_VoidCastFixtureFile_Succeeds()
    {
        var result = ParserTestHelper.ParseFile(
            Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/parser/void_cast.tyhp"));

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindVoidCasts(result.Ast!).Should().HaveCountGreaterThanOrEqualTo(5);
    }

    private static string? VariableName(IExpression? expression)
        => (expression as PhpVariableAst)?.VariableToken?.ValueString;

    private static List<PhpUnaryOpAst> FindVoidCasts(IBase2Ast root)
        => FindUnaryCasts(root, TyhpParser.T_VOID_CAST);

    private static List<PhpUnaryOpAst> FindVoidCastsIn(IBase2Ast? root)
        => root == null ? [] : FindUnaryCasts(root, TyhpParser.T_VOID_CAST);

    private static List<PhpUnaryOpAst> FindUnaryCasts(IBase2Ast root, long tokenType)
    {
        var casts = new List<PhpUnaryOpAst>();
        CollectUnaryCasts(root, tokenType, casts);
        return casts;
    }

    private static void CollectUnaryCasts(IBase2Ast? node, long tokenType, List<PhpUnaryOpAst> casts)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpUnaryOpAst unary
            && unary.IsPrefix
            && unary.Operator?.ValueInt64 == tokenType)
        {
            casts.Add(unary);
        }

        foreach (var child in node.AstChildren)
        {
            CollectUnaryCasts(child, tokenType, casts);
        }
    }

    private static List<PhpLoopAst> FindForLoops(IBase2Ast root)
    {
        var loops = new List<PhpLoopAst>();
        CollectForLoops(root, loops);
        return loops;
    }

    private static void CollectForLoops(IBase2Ast? node, List<PhpLoopAst> loops)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpLoopAst { LoopType: PhpLoopType.For } forLoop)
        {
            loops.Add(forLoop);
        }

        foreach (var child in node.AstChildren)
        {
            CollectForLoops(child, loops);
        }
    }
}
