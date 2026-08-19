using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DocumentHighlightHandlerTests
{
    private const string Source = """
        <?tyhp

        class User {
            public string $name;

            public function greet(): string {
                return "Hi, I'm " . $this->name;
            }
        }

        function createUser(): User {
            $user = new User();
            $user->name = "Alice";
            return $user;
        }
        """;

    [Fact]
    public async Task DocumentHighlight_OnProperty_HighlightsReadAndWriteInSameFile()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hl-property.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        DocumentHighlight[] highlights = await RequestHighlightsAsync(
            session,
            uri,
            PositionOf(Source, "public string $name"));
        highlights.Should().HaveCountGreaterThanOrEqualTo(3);
        highlights.Should().Contain(item => item.Kind == DocumentHighlightKind.Write);
        highlights.Should().Contain(item => item.Kind == DocumentHighlightKind.Read);
    }

    [Fact]
    public async Task DocumentHighlight_OnLocalVariable_HighlightsAssignmentAsWrite()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hl-local.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        DocumentHighlight[] highlights = await RequestHighlightsAsync(
            session,
            uri,
            PositionOf(Source, "return $user"));
        highlights.Should().HaveCountGreaterThanOrEqualTo(2);
        highlights.Should().Contain(item => item.Kind == DocumentHighlightKind.Write);
        highlights.Should().Contain(item => item.Kind == DocumentHighlightKind.Read);
    }

    [Fact]
    public async Task DocumentHighlight_OnMethod_CallSiteIsReadNotWrite()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hl-method-call.tyhp");
        const string source = """
            <?tyhp
            class User {
                public function greet(): string { return "hi"; }
            }
            function run(User $user): string { return $user->greet(); }
            """;
        await OpenAndWaitAsync(session, uri, source);

        DocumentHighlight[] highlights = await RequestHighlightsAsync(
            session,
            uri,
            PositionOf(source, "function greet"));
        highlights.Should().HaveCountGreaterThanOrEqualTo(2);
        highlights.Should().NotContain(item => item.Kind == DocumentHighlightKind.Write);
    }

    [Fact]
    public async Task DocumentHighlight_DoesNotIncludeOtherFiles()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri userUri = FileUri("hl-cross-user.tyhp");
        Uri mainUri = FileUri("hl-cross-main.tyhp");
        const string userSource = "<?tyhp\nnamespace App;\nclass User {}\n";
        const string mainSource = "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n";
        await OpenAndWaitAsync(session, userUri, userSource);
        await OpenAndWaitAsync(session, mainUri, mainSource);

        DocumentHighlight[] highlights = await RequestHighlightsAsync(
            session,
            userUri,
            PositionOf(userSource, "class User"));
        highlights.Should().NotBeEmpty();
        highlights.Should().OnlyContain(item => item.Range.Start.Line == PositionOf(userSource, "class User").Line);
    }

    [Fact]
    public async Task DocumentHighlight_OnWhitespace_ReturnsEmpty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hl-ws.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        DocumentHighlight[] highlights = await RequestHighlightsAsync(
            session,
            uri,
            new Position { Line = 1, Character = 0 });
        highlights.Should().BeEmpty();
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

    private static Task<DocumentHighlight[]> RequestHighlightsAsync(LspTestSession session, Uri uri, Position position)
    {
        return session.Client.InvokeWithParameterObjectAsync<DocumentHighlight[]>(
            Methods.TextDocumentDocumentHighlightName,
            new DocumentHighlightParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = position,
            });
    }

    private static Position PositionOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist in source");
        int offset = index;
        if (needle.StartsWith("class ", StringComparison.Ordinal)
            || needle.StartsWith("function ", StringComparison.Ordinal)
            || needle.StartsWith("public ", StringComparison.Ordinal)
            || needle.StartsWith("return ", StringComparison.Ordinal))
        {
            int lastSpace = needle.LastIndexOf(' ');
            if (lastSpace >= 0)
            {
                offset = index + lastSpace + 1;
            }
        }

        return PositionUtilities.GetPosition(source, offset);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-highlight-tests", fileName);
        return new Uri(path);
    }
}
