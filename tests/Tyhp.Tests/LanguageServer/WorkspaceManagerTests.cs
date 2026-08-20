using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Workspace;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class WorkspaceManagerTests
{
    [Fact]
    public void OpenDocument_TracksContentVersionAndLanguageMode()
    {
        using var workspace = new WorkspaceManager();
        Uri uri = FileUri("example.tyhp");

        DocumentState state = workspace.OpenDocument(uri, "<?tyhp\n", version: 1);

        state.Uri.Should().Be(uri);
        state.Content.Should().Be("<?tyhp\n");
        state.Version.Should().Be(1);
        state.IsDirty.Should().BeTrue();
        state.LanguageMode.Should().Be(WorkspaceManager.LanguageModeTyhp);
        state.ParsedAst.Should().BeNull();
        workspace.GetDocument(uri).Should().BeSameAs(state);
        workspace.GetAllDocuments().Should().ContainSingle().Which.Should().BeSameAs(state);
    }

    [Fact]
    public void DetectLanguageMode_ChecksTyhpdefBeforeTyhp()
    {
        WorkspaceManager.DetectLanguageMode("src/Types.tyhpdef").Should().Be(WorkspaceManager.LanguageModeTyhpdef);
        WorkspaceManager.DetectLanguageMode("src/App.tyhp").Should().Be(WorkspaceManager.LanguageModeTyhp);
        WorkspaceManager.DetectLanguageMode("src/legacy.php").Should().Be(WorkspaceManager.LanguageModePhp);
        WorkspaceManager.DetectLanguageMode("README.md").Should().Be(WorkspaceManager.LanguageModePhp);
    }

    [Fact]
    public void GetDocumentsByLanguageMode_FiltersOpenDocuments()
    {
        using var workspace = new WorkspaceManager();
        workspace.OpenDocument(FileUri("a.tyhp"), "a", 1);
        workspace.OpenDocument(FileUri("b.tyhpdef"), "b", 1);
        workspace.OpenDocument(FileUri("c.php"), "c", 1);

        workspace.GetDocumentsByLanguageMode(WorkspaceManager.LanguageModeTyhp).Should().ContainSingle()
            .Which.FilePath.Should().EndWith("a.tyhp");
        workspace.GetDocumentsByLanguageMode(WorkspaceManager.LanguageModeTyhpdef).Should().ContainSingle()
            .Which.FilePath.Should().EndWith("b.tyhpdef");
        workspace.GetDocumentsByLanguageMode(WorkspaceManager.LanguageModePhp).Should().ContainSingle()
            .Which.FilePath.Should().EndWith("c.php");
    }

    [Fact]
    public void UpdateDocument_AppliesSequentialIncrementalEdits()
    {
        using var workspace = new WorkspaceManager();
        Uri uri = FileUri("edit.tyhp");
        workspace.OpenDocument(uri, "hello world", version: 1);

        DocumentState? afterFirst = workspace.UpdateDocument(
            uri,
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(0, 6), End = new Position(0, 11) },
                    Text = "tyhp",
                },
            ],
            version: 2);

        afterFirst.Should().NotBeNull();
        afterFirst!.Content.Should().Be("hello tyhp");
        afterFirst.Version.Should().Be(2);
        afterFirst.IsDirty.Should().BeTrue();
        afterFirst.ParsedAst.Should().BeNull();

        DocumentState? afterSecond = workspace.UpdateDocument(
            uri,
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(0, 0), End = new Position(0, 5) },
                    Text = "hey",
                },
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(0, 3), End = new Position(0, 3) },
                    Text = "!",
                },
            ],
            version: 3);

        afterSecond!.Content.Should().Be("hey! tyhp");
        afterSecond.Version.Should().Be(3);
        workspace.GetDocument(uri)!.Content.Should().Be("hey! tyhp");
    }

    [Fact]
    public void UpdateDocument_FullReplacement_WhenRangeIsNull()
    {
        using var workspace = new WorkspaceManager();
        Uri uri = FileUri("replace.tyhp");
        workspace.OpenDocument(uri, "old content", version: 1);

        DocumentState? updated = workspace.UpdateDocument(
            uri,
            [new TextDocumentContentChangeEvent { Text = "<?tyhp\nnew file\n" }],
            version: 2);

        updated!.Content.Should().Be("<?tyhp\nnew file\n");
        updated.Version.Should().Be(2);
    }

    [Fact]
    public void ApplyIncrementalChanges_InsertDeleteAndMultiline()
    {
        string inserted = WorkspaceManager.ApplyIncrementalChanges(
            "ab",
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(0, 1), End = new Position(0, 1) },
                    Text = "X",
                },
            ]);
        inserted.Should().Be("aXb");

        string deleted = WorkspaceManager.ApplyIncrementalChanges(
            "aXb",
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(0, 1), End = new Position(0, 2) },
                    Text = "",
                },
            ]);
        deleted.Should().Be("ab");

        string multiline = WorkspaceManager.ApplyIncrementalChanges(
            "one\ntwo\nthree",
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(1, 0), End = new Position(2, 5) },
                    Text = "TWO\nTHREE",
                },
            ]);
        multiline.Should().Be("one\nTWO\nTHREE");
    }

    [Fact]
    public void ApplyIncrementalChanges_HandlesCrlfAndUtf16Emoji()
    {
        string crlf = WorkspaceManager.ApplyIncrementalChanges(
            "a\r\nb\r\nc",
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(1, 0), End = new Position(1, 1) },
                    Text = "B",
                },
            ]);
        crlf.Should().Be("a\r\nB\r\nc");

        // U+1F600 GRINNING FACE is two UTF-16 code units. LSP character 1 is the emoji start;
        // character 3 is 'b'.
        string withEmoji = WorkspaceManager.ApplyIncrementalChanges(
            "a😀b",
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new ProtocolRange { Start = new Position(0, 1), End = new Position(0, 3) },
                    Text = "Z",
                },
            ]);
        withEmoji.Should().Be("aZb");
    }

    [Fact]
    public void UpdateDocument_UnknownUri_ReturnsNull()
    {
        using var workspace = new WorkspaceManager();
        workspace.UpdateDocument(
            FileUri("missing.tyhp"),
            [new TextDocumentContentChangeEvent { Text = "x" }],
            version: 1).Should().BeNull();
    }

    [Fact]
    public void CloseDocument_RemovesStateAndCancelsAnalysis()
    {
        using var workspace = new WorkspaceManager();
        Uri uri = FileUri("close.tyhp");
        DocumentState state = workspace.OpenDocument(uri, "content", 1);
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        state.AnalysisCancellation = cts;

        workspace.CloseDocument(uri).Should().BeTrue();
        workspace.GetDocument(uri).Should().BeNull();
        workspace.GetAllDocuments().Should().BeEmpty();
        token.IsCancellationRequested.Should().BeTrue();
        state.Content.Should().BeEmpty();
    }

    [Fact]
    public void OpenCloseCycles_DoNotLeaveTrackedDocuments()
    {
        using var workspace = new WorkspaceManager();
        Uri uri = FileUri("cycle.tyhp");

        for (int i = 0; i < 50; i++)
        {
            workspace.OpenDocument(uri, new string('x', 1024), i);
            workspace.UpdateDocument(
                uri,
                [
                    new TextDocumentContentChangeEvent
                    {
                        Range = new ProtocolRange { Start = new Position(0, 0), End = new Position(0, 1) },
                        Text = "y",
                    },
                ],
                i + 1);
            workspace.CloseDocument(uri).Should().BeTrue();
        }

        workspace.GetAllDocuments().Should().BeEmpty();
        workspace.GetDocument(uri).Should().BeNull();
    }

    [Fact]
    public void OpenDocument_ReplacesExistingDocumentAtSameUri()
    {
        using var workspace = new WorkspaceManager();
        Uri uri = FileUri("reopen.tyhp");
        DocumentState first = workspace.OpenDocument(uri, "first", 1);
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        first.AnalysisCancellation = cts;

        DocumentState second = workspace.OpenDocument(uri, "second", 4);

        workspace.GetAllDocuments().Should().ContainSingle();
        second.Content.Should().Be("second");
        second.Version.Should().Be(4);
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void GetUtf16Offset_ClampsPastEndOfLineAndDocument()
    {
        WorkspaceManager.GetUtf16Offset("ab", new Position(0, 50)).Should().Be(2);
        WorkspaceManager.GetUtf16Offset("ab\ncd", new Position(9, 0)).Should().Be(5);
        WorkspaceManager.GetUtf16Offset("", new Position(0, 0)).Should().Be(0);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-workspace-tests", fileName);
        return new Uri(path);
    }
}
