using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Token-name parity between <see cref="TyhpLexer"/> and <see cref="TyhpdefLexer"/>
/// on existing tyhpdef fixtures. Type integers may differ; names, channels, and text may not.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefLexerTokenParityTests
{
    private static readonly string[] StringModes =
    [
        "ST_DOUBLE_QUOTES",
        "ST_BACKQUOTE",
        "ST_HEREDOC",
        "ST_NOWDOC",
    ];

    [Fact]
    public void TaggedAndTaglessFixtures_TokenNamesChannelsAndTextMatch()
    {
        var tagged = Path.Combine(
            TestFileManager.GetTestDataDirectory(),
            "ValidTyhpdef",
            "parser",
            "function_definitions.tyhpdef");
        var tagless = Path.Combine(
            TestFileManager.GetConformanceDirectory(),
            "story06",
            "tagless",
            "tagless_tyhpdef.tyhpdef");

        Compare(tagged, tagless: false);
        Compare(tagless, tagless: true);
    }

    /// <summary>
    /// The two fixtures above only exercise <c>async</c> and single-quoted strings, so a
    /// dropped tyhpdef keyword rule (<c>extern</c>/<c>deprecated</c>/<c>obsolete</c>/<c>partial</c>)
    /// or a dropped string mode (heredoc) would not fail token-name parity. This fixture combines
    /// declarations already proven to parse by <see cref="TyhpdefExternParseTests"/>,
    /// <see cref="TyhpdefContextualKeywordParseTests"/>, and <see cref="TyhpdefPartialFunctionParseTests"/>
    /// with a heredoc constant so the comparison actually walks those lexer rules and the
    /// <c>ST_HEREDOC</c> mode transition.
    /// </summary>
    [Fact]
    public void KeywordAndHeredocFixture_TokenNamesChannelsAndTextMatch()
    {
        var path = Path.Combine(
            TestFileManager.GetTestDataDirectory(),
            "ValidTyhpdef",
            "parser",
            "keyword_coverage.tyhpdef");

        var source = File.ReadAllText(path);
        source.Should().Contain("extern class").And.Contain("deprecated function")
            .And.Contain("obsolete function").And.Contain("partial function")
            .And.Contain("async function").And.Contain("<<<EOT");

        Compare(path, tagless: false);
    }

    private static void Compare(string path, bool tagless)
    {
        var source = File.ReadAllText(path);
        var tyhp = LexTyhp(source, path, tagless);
        var tyhpdef = LexTyhpdef(source, path, tagless);

        tyhpdef.Tokens.Select(t => (t.Name, t.Channel, t.Text)).Should().Equal(
            tyhp.Tokens.Select(t => (t.Name, t.Channel, t.Text)),
            $"{path} token names, channels, and text must match");
        tyhpdef.ModeCommands.Should().Equal(
            tyhp.ModeCommands,
            $"{path} lexer mode transitions must match");

        if (tagless && !source.Contains("<?tyhpdef", StringComparison.Ordinal))
        {
            tyhpdef.ModeCommands.Should().Contain("ST_IN_SCRIPTING");
        }
        else
        {
            tyhpdef.ModeCommands.Should().Contain("ST_CHECK_FOR_OTHER_OPEN_TAGS_LEXER_ADDON");
            tyhpdef.ModeCommands.Should().Contain("ST_IN_SCRIPTING");
        }

        if (tyhp.ModeCommands.Any(mode => StringModes.Contains(mode)))
        {
            tyhpdef.ModeCommands.Should().Contain(mode => StringModes.Contains(mode));
        }
    }

    private static LexResult LexTyhp(string source, string fileName, bool tagless)
    {
        var lexer = new TracingTyhpLexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(tagless, tagless ? "tyhpdef" : string.Empty, new DiagnosticBag(), fileName);
        return Lex(lexer, lexer.ModeCommands);
    }

    private static LexResult LexTyhpdef(string source, string fileName, bool tagless)
    {
        var lexer = new TracingTyhpdefLexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(tagless, tagless ? "tyhpdef" : string.Empty, new DiagnosticBag(), fileName);
        return Lex(lexer, lexer.ModeCommands);
    }

    private static LexResult Lex(Antlr4.Runtime.Lexer lexer, List<string> modeCommands)
    {
        var vocabulary = lexer.Vocabulary;
        var tokens = new List<LexedToken>();
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Type == Antlr4.Runtime.Lexer.Eof)
            {
                break;
            }

            var name = vocabulary.GetSymbolicName(token.Type)
                ?? vocabulary.GetDisplayName(token.Type);
            tokens.Add(new LexedToken(name, token.Channel, token.Text ?? ""));
        }

        return new LexResult(tokens, modeCommands);
    }

    private readonly record struct LexedToken(string Name, int Channel, string Text);

    private readonly record struct LexResult(List<LexedToken> Tokens, List<string> ModeCommands);

    private sealed class TracingTyhpLexer : TyhpLexer
    {
        public TracingTyhpLexer(ICharStream input)
            : base(input)
        {
        }

        public List<string> ModeCommands { get; } = new();

        public override void PushMode(int m)
        {
            this.ModeCommands.Add(TyhpLexer.modeNames[m]);
            base.PushMode(m);
        }

        public override int PopMode()
        {
            var mode = base.PopMode();
            this.ModeCommands.Add(TyhpLexer.modeNames[mode]);
            return mode;
        }

        public override void Mode(int m)
        {
            this.ModeCommands.Add(TyhpLexer.modeNames[m]);
            base.Mode(m);
        }
    }

    private sealed class TracingTyhpdefLexer : TyhpdefLexer
    {
        public TracingTyhpdefLexer(ICharStream input)
            : base(input)
        {
        }

        public List<string> ModeCommands { get; } = new();

        public override void PushMode(int m)
        {
            this.ModeCommands.Add(TyhpdefLexer.modeNames[m]);
            base.PushMode(m);
        }

        public override int PopMode()
        {
            var mode = base.PopMode();
            this.ModeCommands.Add(TyhpdefLexer.modeNames[mode]);
            return mode;
        }

        public override void Mode(int m)
        {
            this.ModeCommands.Add(TyhpdefLexer.modeNames[m]);
            base.Mode(m);
        }
    }
}
