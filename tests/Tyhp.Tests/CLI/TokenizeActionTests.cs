using System.Text.Json;
using Antlr4.Runtime;
using Tyhp.CLI;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.CLI;

/// <summary>
/// <c>tokenize</c> prints symbolic token names from the lexer that produced the stream.
/// Tyhp and tyhpdef token ids are independent, so a <c>.tyhpdef</c> file must use
/// <see cref="TyhpdefLexer"/>'s vocabulary.
/// </summary>
[Trait("Category", "CLI")]
public class TokenizeActionTests
{
    [Fact]
    public void Tokenize_TyhpdefFile_UsesTyhpdefVocabulary()
    {
        var tokens = TokenizeToArray("Month.tyhpdef", """
            <?tyhpdef
            extern enum enum\Acme\Month : int;
            """, TyhpdefLexer.DefaultVocabulary);

        TokenText(tokens, "extern").GetProperty("type").GetString().Should().Be("T_TYHPDEF_EXTERN");
        tokens.EnumerateArray().Should().Contain(token =>
            token.GetProperty("type").GetString() == "T_WHITESPACE"
            && string.IsNullOrWhiteSpace(token.GetProperty("text").GetString()));
        TokenText(tokens, @"enum\Acme\Month").GetProperty("type").GetString()
            .Should().Be("T_NAME_QUALIFIED");
    }

    [Fact]
    public void Tokenize_TyhpFile_UsesTyhpVocabulary()
    {
        var tokens = TokenizeToArray("App.tyhp", """
            <?tyhp
            function demo(): void {
            }
            """, TyhpLexer.DefaultVocabulary);

        TokenText(tokens, "function").GetProperty("type").GetString().Should().Be("T_FUNCTION");
    }

    private static JsonElement TokenizeToArray(string fileName, string source, IVocabulary vocabulary)
    {
        using var builder = new TestProjectBuilder();
        var sourcePath = Path.Combine(builder.ProjectDirectory, fileName);
        builder.WithTyhpFile(fileName, source);
        builder.WithConfigValue("path:0", sourcePath);
        builder.WithConfigValue("quiet", "true");
        var project = builder.BuildProject();
        var outPath = Path.Combine(builder.ProjectDirectory, "tokens.json");

        new TokenizeAction(project, outPath, modeOverride: null).Start(CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var file = doc.RootElement.GetProperty("files")[0];
        file.GetProperty("file").GetString().Should().Be(sourcePath);
        var tokens = file.GetProperty("tokens");
        AssertTypeNames(tokens, vocabulary);
        return tokens.Clone();
    }

    private static void AssertTypeNames(JsonElement tokens, IVocabulary vocabulary)
    {
        tokens.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var token in tokens.EnumerateArray())
        {
            var typeId = token.GetProperty("typeId").GetInt32();
            var expected = typeId == TokenConstants.EOF
                ? "EOF"
                : vocabulary.GetSymbolicName(typeId)
                    ?? vocabulary.GetDisplayName(typeId)
                    ?? typeId.ToString();
            token.GetProperty("type").GetString().Should().Be(
                expected,
                "token text '{0}' typeId {1} must be named by the producing lexer",
                token.GetProperty("text").GetString(),
                typeId);
        }
    }

    private static JsonElement TokenText(JsonElement tokens, string text)
    {
        foreach (var token in tokens.EnumerateArray())
        {
            if (token.GetProperty("text").GetString() == text)
            {
                return token;
            }
        }

        throw new InvalidOperationException($"No token with text '{text}'.");
    }
}
