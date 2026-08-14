using System.Text;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Lexer;

/// <summary>
/// <c>(unset)</c> / <c>T_UNSET_CAST</c> is removed from Tyhp (deprecated in PHP 8.5).
/// These tests pin that the cast token is not emitted and that usage is rejected.
/// </summary>
[Trait("Category", "Lexer")]
public class UnsetCastRemovedTests
{
    [Theory]
    [InlineData("(unset)")]
    [InlineData("( unset )")]
    [InlineData("(UNSET)")]
    public void Lex_UnsetCast_LexesAsParenUnsetKeywordParen_NotSingleCastToken(string castText)
    {
        var tokens = LexDefaultChannel($"""
            <?php
            {castText}$x;
            """);

        // Without T_UNSET_CAST, `(unset)` is open paren + unset keyword + close paren.
        var beforeVar = tokens.TakeWhile(t => t.Type != TyhpLexer.T_VARIABLE).ToList();
        beforeVar.Select(t => t.Type).Should().ContainInOrder(
            TyhpLexer.T_OPEN_ROUND_BRACE,
            TyhpLexer.T_UNSET,
            TyhpLexer.T_CLOSE_ROUND_BRACE);
        // Must not be a single opaque cast token spanning the whole `(unset)`.
        beforeVar.Should().NotContain(t => t.Text.Equals(castText, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_ValidCasts_StillWork_AndUnsetKeywordStatementStillLexes()
    {
        var tokens = LexDefaultChannel("""
            <?php
            (int)$a;
            (void)$b;
            unset($c);
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_INT_CAST);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_VOID_CAST);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_UNSET);
    }

    [Fact]
    public void Parse_UnsetCastExpression_IsRejected()
    {
        var result = ParserTestHelper.ParsePhpContent("""
            <?php
            $y = (unset)$x;
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "`(unset)$x` must not parse — unset cast is not part of Tyhp’s PHP 8.5 grammar");
    }

    [Fact]
    public void Parse_UnsetCastStatement_IsRejected()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            (unset)$x;
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "`(unset)$x` must not parse — unset cast is not part of Tyhp’s PHP 8.5 grammar");
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
