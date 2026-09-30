using System.Text;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Lexer;

/// <summary>
/// <c>//</c> and <c>#</c> comments end at <c>?&gt;</c>. A <c>?</c> that is not that closer,
/// including a URL query, stays inside the comment through the end of the line.
/// Block comments are not closed by <c>?&gt;</c>.
/// </summary>
[Trait("Category", "Lexer")]
public class LineCommentCloseTagTests
{
    [Fact]
    public void Lex_LineComment_WithoutCloseTag_ConsumesRestOfLine()
    {
        var tokens = LexAll("""
            <?php
            // see https://example.com/path?x=1 and what?
            $a = 1;
            # see https://example.com/path?y=2 and what?
            $b = 2;
            /* block ?> still comment */
            $c = 3;
            """);

        CommentTexts(tokens).Should().Equal(
            "// see https://example.com/path?x=1 and what?",
            "# see https://example.com/path?y=2 and what?",
            "/* block ?> still comment */");
        DefaultTexts(tokens).Should().ContainInOrder("$a", "$b", "$c");
    }

    [Fact]
    public void Lex_LineComment_EndsBeforeCloseTag()
    {
        var tokens = LexAll("""
            <?php
            // tabindex stays client-side ?>
            <div><?php
            # hash ends here ?>
            <span>
            """);

        CommentTexts(tokens).Should().Equal(
            "// tabindex stays client-side ",
            "# hash ends here ");
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_INLINE_HTML && t.Text.Contains("<div>"));
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_INLINE_HTML && t.Text.Contains("<span>"));
    }

    [Fact]
    public void Lex_LineComment_EndsBeforeCloseTagAtEof()
    {
        var slash = LexAll("<?php // end ?><div>");
        CommentTexts(slash).Should().Equal("// end ");
        slash.Should().Contain(t => t.Type == TyhpLexer.T_INLINE_HTML && t.Text.Contains("<div>"));

        var hash = LexAll("<?php # hash ?><span>");
        CommentTexts(hash).Should().Equal("# hash ");
        hash.Should().Contain(t => t.Type == TyhpLexer.T_INLINE_HTML && t.Text.Contains("<span>"));
    }

    [Fact]
    public void Lex_LineComment_WithoutCloseTag_ReachesEof()
    {
        CommentTexts(LexAll("<?php // trailing comment")).Should().Equal("// trailing comment");
        CommentTexts(LexAll("<?php # trailing hash")).Should().Equal("# trailing hash");
    }

    private static List<string> CommentTexts(IReadOnlyList<IToken> tokens)
        => tokens.Where(t => t.Type == TyhpLexer.T_COMMENT).Select(t => t.Text).ToList();

    private static List<string> DefaultTexts(IReadOnlyList<IToken> tokens)
        => tokens
            .Where(t => t.Channel == Antlr4.Runtime.Lexer.DefaultTokenChannel && t.Type != TyhpLexer.Eof)
            .Select(t => t.Text)
            .ToList();

    private static List<IToken> LexAll(string source)
    {
        var contentBytes = Encoding.UTF8.GetBytes(source);
        var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
        var lexer = new TyhpLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(enabled: false, languageMode: string.Empty, new DiagnosticBag(), fileName: "test.php");

        var stream = new CommonTokenStream(lexer);
        stream.Fill();
        return stream.GetTokens().Where(t => t.Type != TyhpLexer.Eof).ToList();
    }
}
