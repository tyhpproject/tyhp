using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class SelectionRangeHandlerTests
{
    private const string Source = """
        <?tyhp

        class User {
            public function greet(): string {
                return "Hi, I'm " . $this->name;
            }
        }
        """;

    [Fact]
    public async Task SelectionRange_ExpandsFromTokenToFile()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("select.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Position namePos = PositionOf(Source, "$this->name");
        namePos = new Position { Line = namePos.Line, Character = namePos.Character + "$this->".Length };

        SelectionRange[] ranges = await session.Client.InvokeWithParameterObjectAsync<SelectionRange[]>(
            TyhpLanguageServer.TextDocumentSelectionRangeName,
            new SelectionRangeParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Positions = [namePos],
            });
        ranges.Should().HaveCount(1);
        SelectionRange current = ranges[0];
        var chain = new List<Microsoft.VisualStudio.LanguageServer.Protocol.Range>();
        while (current is not null)
        {
            current.Range.Should().NotBeNull();
            chain.Add(current.Range!);
            current = current.Parent!;
        }

        chain.Count.Should().BeGreaterThanOrEqualTo(3);
        ContainsRange(chain[^1], chain[0]).Should().BeTrue();
        chain[^1].Start.Line.Should().Be(0);
        chain.Should().Contain(range => range.Start.Line == PositionOf(Source, "function greet").Line);
        chain.Should().Contain(range => range.Start.Line == PositionOf(Source, "class User").Line);
    }

    [Fact]
    public async Task SelectionRange_EmptyPositions_ReturnsEmpty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("select-empty.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        SelectionRange[] ranges = await session.Client.InvokeWithParameterObjectAsync<SelectionRange[]>(
            TyhpLanguageServer.TextDocumentSelectionRangeName,
            new SelectionRangeParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Positions = [],
            });
        ranges.Should().BeEmpty();
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

    private static Position PositionOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        return PositionUtilities.GetPosition(source, index);
    }

    private static bool ContainsRange(
        Microsoft.VisualStudio.LanguageServer.Protocol.Range outer,
        Microsoft.VisualStudio.LanguageServer.Protocol.Range inner)
    {
        return Compare(outer.Start, inner.Start) <= 0 && Compare(inner.End, outer.End) <= 0;
    }

    private static int Compare(Position a, Position b)
    {
        int line = a.Line.CompareTo(b.Line);
        return line != 0 ? line : a.Character.CompareTo(b.Character);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-selection-tests", fileName);
        return new Uri(path);
    }
}
