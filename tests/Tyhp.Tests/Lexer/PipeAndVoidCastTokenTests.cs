using System.Text;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Lexer;

/// <summary>
/// Lexer-only coverage for PHP 8.5 tokens <c>T_PIPE</c> (<c>|&gt;</c>) and
/// <c>T_VOID_CAST</c> (<c>(void)</c>). See <c>PipeOperatorParseTests</c> for pipe parse/AST.
/// </summary>
[Trait("Category", "Lexer")]
public class PipeAndVoidCastTokenTests
{
    [Fact]
    public void Lex_PipeOperator_EmitsTPipeNotSeparateBarAndGt()
    {
        var tokens = LexDefaultChannel("""
            <?php
            $a |> $b;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_PIPE);
        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_SYM_PIPE && t.Text == "|");
        // A lone '>' must not appear for the pipe operator itself.
        var pipeIndex = tokens.FindIndex(t => t.Type == TyhpLexer.T_PIPE);
        pipeIndex.Should().BeGreaterThanOrEqualTo(0);
        tokens[pipeIndex].Text.Should().Be("|>");
    }

    [Fact]
    public void Lex_OrEqualAndGt_StillSeparateFromPipe()
    {
        var tokens = LexDefaultChannel("""
            <?php
            $a |= $b;
            $c > $d;
            $e | $f;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_OR_EQUAL);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_SYM_GT);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_SYM_PIPE);
        tokens.Select(t => t.Type).Should().NotContain(TyhpLexer.T_PIPE);
    }

    [Theory]
    [InlineData("(void)")]
    [InlineData("( void )")]
    [InlineData("(  void  )")]
    [InlineData("(\tvoid\t)")]
    [InlineData("(VOID)")]
    [InlineData("(Void)")]
    public void Lex_VoidCast_EmitsTVoidCast(string castText)
    {
        var tokens = LexDefaultChannel($"""
            <?php
            {castText} $x;
            """);

        var cast = tokens.Should().ContainSingle(t => t.Type == TyhpLexer.T_VOID_CAST).Subject;
        cast.Text.Should().Be(castText);
    }

    [Fact]
    public void Lex_VoidCast_DoesNotEmitOpenParenAndVoidSeparately()
    {
        var tokens = LexDefaultChannel("""
            <?php
            (void)$x;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_VOID_CAST);
        // Opening '(' of the cast must not be a standalone round-brace token.
        var beforeVar = tokens.TakeWhile(t => t.Type != TyhpLexer.T_VARIABLE).ToList();
        beforeVar.Should().NotContain(t => t.Type == TyhpLexer.T_OPEN_ROUND_BRACE);
        beforeVar.Should().NotContain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("void", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_IntCast_StillWorksAlongsideVoidCast()
    {
        var tokens = LexDefaultChannel("""
            <?php
            (int)$a;
            (void)$b;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_INT_CAST);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_VOID_CAST);
    }

    [Fact]
    public void Lex_BareVoidKeywordInTyhp_RemainsTTyhpVoidNotCast()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            function f(): void {}
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHP_VOID);
        tokens.Select(t => t.Type).Should().NotContain(TyhpLexer.T_VOID_CAST);
    }

    private static List<IToken> LexDefaultChannel(string source)
    {
        var contentBytes = Encoding.UTF8.GetBytes(source);
        var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
        var lexer = new TyhpLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(enabled: false, languageMode: string.Empty, new DiagnosticBag(), fileName: "test.php");

        var stream = new CommonTokenStream(lexer);
        stream.Fill();

        return stream.GetTokens()
            .Where(t => t.Type != TyhpLexer.Eof && t.Channel == Antlr4.Runtime.Lexer.DefaultTokenChannel)
            .ToList();
    }
}
