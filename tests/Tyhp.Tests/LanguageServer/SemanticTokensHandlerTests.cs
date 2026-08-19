using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class SemanticTokensHandlerTests
{
    private const string Source = """
        <?tyhp

        class User {
            public string $name;

            /**
             * @deprecated Use display() instead
             */
            public function greet(): string {
                return "Hi, I'm " . $this->name;
            }

            public static function make(string $label): User {
                $user = new User();
                $user->name = $label;
                return $user;
            }
        }

        function createUser(string $title): User {
            return User::make($title);
        }

        enum Color {
            case Red;
        }

        type Box<T> = T;
        """;

    [Fact]
    public async Task SemanticTokensFull_EncodesValidFiveTuples()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        SemanticTokens tokens = await RequestFullAsync(session, uri);
        tokens.Data.Should().NotBeNull();
        tokens.Data!.Length.Should().BeGreaterThan(0);
        (tokens.Data.Length % 5).Should().Be(0);
        tokens.ResultId.Should().NotBeNullOrEmpty();

        IReadOnlyList<DecodedSemanticToken> decoded = SemanticTokenCollector.Decode(tokens.Data);
        decoded.Should().NotBeEmpty();
        for (int i = 1; i < decoded.Count; i++)
        {
            DecodedSemanticToken previous = decoded[i - 1];
            DecodedSemanticToken current = decoded[i];
            current.Line.Should().BeGreaterThanOrEqualTo(previous.Line);
            if (current.Line == previous.Line)
            {
                current.Character.Should().BeGreaterThanOrEqualTo(previous.Character);
            }
        }
    }

    [Fact]
    public async Task SemanticTokensFull_ParametersAndVariablesHaveDifferentTypes()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-vars.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Parameter
            && TokenText(Source, token).Contains("title", StringComparison.Ordinal));
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Variable
            && TokenText(Source, token).Contains("user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SemanticTokensFull_TypeAnnotationsAreTypeTokens()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-types.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);
        Position returnType = PositionOf(Source, "): User");
        returnType = new Position { Line = returnType.Line, Character = returnType.Character + 3 };
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Type
            && token.Line == returnType.Line
            && TokenText(Source, token).Contains("User", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticTokensFull_StaticMembersHaveStaticModifier()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-static.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Method
            && token.Modifiers.Contains(SemanticTokenModifiers.Static)
            && TokenText(Source, token).Contains("make", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticTokensFull_StaticPropertyHasStaticModifier()
    {
        const string StaticPropertySource = """
            <?tyhp

            class Counter {
                public static int $total = 0;
            }
            """;
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-static-property.tyhp");
        await OpenAndWaitAsync(session, uri, StaticPropertySource);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Property
            && token.Modifiers.Contains(SemanticTokenModifiers.Static)
            && TokenText(StaticPropertySource, token).Contains("total", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticTokensFull_UseImport_ColorsNamespaceAndTypeSegments()
    {
        const string ImportSource = """
            <?tyhp

            namespace App\Controllers;

            use App\Models\User;

            function getUser(): User {
                return new User();
            }
            """;
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-use-import.tyhp");
        await OpenAndWaitAsync(session, uri, ImportSource);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);

        // The `use App\Models\User;` line must colorize its namespace segments and
        // imported name, not be skipped entirely (PhpImportDeclAst stores the imported
        // name as a plain string, not a nested PhpNameAst child).
        Position useLine = PositionOf(ImportSource, "use App");
        decoded.Should().Contain(token =>
            token.Line == useLine.Line
            && token.Type == SemanticTokenTypes.Namespace
            && TokenText(ImportSource, token) == "App");
        decoded.Should().Contain(token =>
            token.Line == useLine.Line
            && token.Type == SemanticTokenTypes.Namespace
            && TokenText(ImportSource, token) == "Models");
        decoded.Should().Contain(token =>
            token.Line == useLine.Line
            && TokenText(ImportSource, token) == "User");
    }

    [Fact]
    public async Task SemanticTokensFull_DeprecatedMembersHaveDeprecatedModifier()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-deprecated.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);
        decoded.Should().Contain(token =>
            token.Modifiers.Contains(SemanticTokenModifiers.Deprecated)
            && TokenText(Source, token).Contains("greet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticTokensFull_GenericTypeParametersHaveOwnType()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-generic.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        IReadOnlyList<DecodedSemanticToken> decoded = await DecodeFullAsync(session, uri);
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.TypeParameter
            && TokenText(Source, token) == "T");
    }

    [Fact]
    public async Task SemanticTokensFull_EmptyFile_ReturnsEmptyData()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-empty.tyhp");
        await OpenAndWaitAsync(session, uri, "<?tyhp\n");

        SemanticTokens tokens = await RequestFullAsync(session, uri);
        tokens.Data.Should().NotBeNull();
        tokens.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SemanticTokensDelta_UnknownPreviousResultId_ReturnsFullTokens()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-delta-unknown.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        JObject payload = await session.Client.InvokeWithParameterObjectAsync<JObject>(
            Methods.TextDocumentSemanticTokensFullDeltaName,
            new SemanticTokensDeltaParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                PreviousResultId = "missing",
            });
        payload["data"].Should().NotBeNull("unknown previousResultId must fall back to a full SemanticTokens payload");
        payload["edits"].Should().BeNull();
    }

    [Fact]
    public async Task SemanticTokensDelta_MatchingPreviousResultId_ReturnsEdits()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("tokens-delta.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        SemanticTokens first = await RequestFullAsync(session, uri);
        first.ResultId.Should().NotBeNullOrEmpty();

        SemanticTokensDelta delta = await session.Client.InvokeWithParameterObjectAsync<SemanticTokensDelta>(
            Methods.TextDocumentSemanticTokensFullDeltaName,
            new SemanticTokensDeltaParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                PreviousResultId = first.ResultId,
            });
        delta.Edits.Should().NotBeNull();
        delta.ResultId.Should().NotBeNullOrEmpty();
        delta.ResultId.Should().NotBe(first.ResultId);
    }

    [Fact]
    public void ComputeDelta_IdenticalArrays_ReturnsNoEdits()
    {
        int[] data = [0, 1, 4, 2, 0, 0, 6, 3, 8, 0];
        SemanticTokensEdit[] edits = SemanticTokenCollector.ComputeDelta(data, data);
        edits.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_ReplacesChangedMiddle()
    {
        int[] previous = [1, 2, 3, 4, 5];
        int[] current = [1, 9, 8, 5];
        SemanticTokensEdit[] edits = SemanticTokenCollector.ComputeDelta(previous, current);
        edits.Should().HaveCount(1);
        edits[0].Start.Should().Be(1);
        edits[0].DeleteCount.Should().Be(3);
        edits[0].Data.Should().Equal(9, 8);
    }

    private static async Task<IReadOnlyList<DecodedSemanticToken>> DecodeFullAsync(LspTestSession session, Uri uri)
    {
        SemanticTokens tokens = await RequestFullAsync(session, uri);
        return SemanticTokenCollector.Decode(tokens.Data ?? []);
    }

    private static Task<SemanticTokens> RequestFullAsync(LspTestSession session, Uri uri)
    {
        return session.Client.InvokeWithParameterObjectAsync<SemanticTokens>(
            Methods.TextDocumentSemanticTokensFullName,
            new SemanticTokensParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });
    }

    private static async Task OpenAndWaitAsync(LspTestSession session, Uri uri, string text)
    {
        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri,
                    LanguageId = "tyhp",
                    Version = 1,
                    Text = text,
                },
            });
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null);
    }

    private static string TokenText(string source, DecodedSemanticToken token)
    {
        Position start = new() { Line = token.Line, Character = token.Character };
        int offset = PositionUtilities.GetOffset(source, start);
        int end = Math.Min(source.Length, offset + token.Length);
        return offset >= 0 && offset < end ? source[offset..end] : string.Empty;
    }

    private static Position PositionOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0);
        return PositionUtilities.GetPosition(source, index);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-semantic-tokens-tests", fileName);
        return new Uri(path);
    }
}
