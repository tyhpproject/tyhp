using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Workspace;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DocumentSyncHandlerTests
{
    [Fact]
    public async Task DidOpen_CreatesDocumentState()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("opened.tyhp");
        const string content = "<?tyhp\nfunction f(): void {}\n";

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri,
                    LanguageId = "tyhp",
                    Version = 1,
                    Text = content,
                },
            });

        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri) is not null);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null);

        DocumentState state = session.Server.Workspace.GetDocument(uri)!;
        state.Content.Should().Be(content);
        state.Version.Should().Be(1);
        state.IsDirty.Should().BeFalse();
        state.ParsedAst.Should().NotBeNull();
        state.LanguageMode.Should().Be(WorkspaceManager.LanguageModeTyhp);
        session.Server.Workspace.WorkspaceRoot.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DidChange_AppliesIncrementalAndFullReplacement()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("changed.tyhp");

        await OpenAsync(session, uri, "hello world", version: 1);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.Content == "hello world");

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidChangeName,
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
                ContentChanges =
                [
                    new TextDocumentContentChangeEvent
                    {
                        Range = new ProtocolRange { Start = new Position(0, 6), End = new Position(0, 11) },
                        Text = "tyhp",
                    },
                ],
            });

        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.Content == "hello tyhp");
        session.Server.Workspace.GetDocument(uri)!.Version.Should().Be(2);

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidChangeName,
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 3 },
                ContentChanges = [new TextDocumentContentChangeEvent { Text = "<?tyhp\n" }],
            });

        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.Content == "<?tyhp\n");
        session.Server.Workspace.GetDocument(uri)!.Version.Should().Be(3);
    }

    [Fact]
    public async Task DidClose_RemovesDocument()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("closed.tyhp");

        await OpenAsync(session, uri, "content", version: 1);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri) is not null);

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidCloseName,
            new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });

        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri) is null);
        session.Server.Workspace.GetAllDocuments().Should().BeEmpty();
    }

    [Fact]
    public async Task DidSave_WithText_ReplacesContent()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("saved.tyhp");

        await OpenAsync(session, uri, "unsaved", version: 1);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri) is not null);

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidSaveName,
            new DidSaveTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Text = "saved content",
            });

        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.Content == "saved content");
    }

    [Fact]
    public async Task DidChange_UnknownDocument_DoesNotThrow()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidChangeName,
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier
                {
                    Uri = FileUri("never-opened.tyhp"),
                    Version = 1,
                },
                ContentChanges = [new TextDocumentContentChangeEvent { Text = "x" }],
            });

        session.Server.Workspace.GetAllDocuments().Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatedOpenClose_CleansUpTrackedDocuments()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("cycle-rpc.tyhp");

        for (int i = 0; i < 10; i++)
        {
            await OpenAsync(session, uri, $"v{i}", version: i);
            await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.Content == $"v{i}");

            await session.Client.NotifyWithParameterObjectAsync(
                Methods.TextDocumentDidCloseName,
                new DidCloseTextDocumentParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = uri },
                });
            await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri) is null);
        }

        session.Server.Workspace.GetAllDocuments().Should().BeEmpty();
    }

    private static Task OpenAsync(LspTestSession session, Uri uri, string text, int version)
    {
        return session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri,
                    LanguageId = "tyhp",
                    Version = version,
                    Text = text,
                },
            });
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-docsync-tests", fileName);
        return new Uri(path);
    }
}
