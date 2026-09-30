using System.Text;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Lexer;

/// <summary>
/// <c>T_TYHP_IS</c> is only the word <c>is</c>. Removed spellings lex as identifiers.
/// </summary>
[Trait("Category", "Lexer")]
public class IsOperatorTokenTests
{
    [Fact]
    public void Lex_Is_IsTyhpIsToken()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            function demo(mixed $x): void {
                $ok = $x is Foo;
            }
            """);

        tokens.Should().Contain(t => t.Type == TyhpLexer.T_TYHP_IS && t.Text.Equals("is", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("isa")]
    [InlineData("isan")]
    [InlineData("is_a")]
    [InlineData("is_an")]
    public void Lex_RemovedSpellings_AreNotTyhpIs(string spelling)
    {
        var tokens = LexDefaultChannel($$"""
            <?tyhp
            function demo(mixed $x): void {
                $ok = $x {{spelling}} Foo;
            }
            """);

        tokens.Should().NotContain(
            t => t.Type == TyhpLexer.T_TYHP_IS,
            $"`{spelling}` must not tokenize as T_TYHP_IS");
        tokens.Should().Contain(
            t => t.Type == TyhpLexer.T_STRING && t.Text.Equals(spelling, StringComparison.OrdinalIgnoreCase),
            $"`{spelling}` should lex as T_STRING");
    }

    [Fact]
    public void Lex_Is_InTyhpdefMode_IsTyhpIsToken()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            function demo(mixed $x): $x is Foo;
            """);

        tokens.Should().Contain(t => t.Type == TyhpLexer.T_TYHP_IS && t.Text.Equals("is", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("isa")]
    [InlineData("isan")]
    [InlineData("is_a")]
    [InlineData("is_an")]
    public void Lex_RemovedSpellings_InTyhpdefMode_AreNotTyhpIs(string spelling)
    {
        var tokens = LexDefaultChannel($$"""
            <?tyhpdef
            function demo(mixed $x): $x {{spelling}} Foo;
            """);

        tokens.Should().NotContain(
            t => t.Type == TyhpLexer.T_TYHP_IS,
            $"`{spelling}` must not tokenize as T_TYHP_IS in tyhpdef mode");
        tokens.Should().Contain(
            t => t.Type == TyhpLexer.T_STRING && t.Text.Equals(spelling, StringComparison.OrdinalIgnoreCase),
            $"`{spelling}` should lex as T_STRING in tyhpdef mode");
    }

    [Fact]
    public void Lex_IsAFunctionCall_IsNotTyhpIs()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            function demo(object $obj): void {
                $ok = \is_a($obj, User::class);
            }
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHP_IS);
        tokens.Should().Contain(t =>
            t.Text.Contains("is_a", StringComparison.OrdinalIgnoreCase)
            && t.Type != TyhpLexer.T_TYHP_IS);
    }

    private static List<IToken> LexDefaultChannel(string source)
    {
        var contentBytes = Encoding.UTF8.GetBytes(source);
        var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
        var lexer = new TyhpLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(enabled: false, languageMode: string.Empty, new DiagnosticBag(), fileName: "test.tyhp");

        var stream = new CommonTokenStream(lexer);
        stream.Fill();

        return stream.GetTokens()
            .Where(t => t.Type != TyhpLexer.Eof && t.Channel == Antlr4.Runtime.Lexer.DefaultTokenChannel)
            .ToList();
    }
}
