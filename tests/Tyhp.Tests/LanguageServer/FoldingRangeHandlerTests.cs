using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class FoldingRangeHandlerTests
{
    private const string Source = """
        <?tyhp

        use App\A;
        use App\B;

        /**
         * A user record.
         * Second line of docs.
         */
        class User {
            public string $name;

            public function greet(): string {
                return "Hi";
            }
        }

        function createUser(): User {
            $values = [
                1,
                2,
                3,
            ];
            return new User();
        }
        """;

    [Fact]
    public async Task FoldingRange_IncludesClassAndFunctionBodies()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fold.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        FoldingRange[] ranges = await RequestFoldingAsync(session, uri);
        ranges.Should().NotBeEmpty();
        ranges.Should().Contain(range =>
            range.Kind == FoldingRangeKind.Region
            && range.StartLine == PositionOf(Source, "class User").Line);
        ranges.Should().Contain(range =>
            range.Kind == FoldingRangeKind.Region
            && range.StartLine == PositionOf(Source, "function greet").Line);
        ranges.Should().Contain(range =>
            range.Kind == FoldingRangeKind.Region
            && range.StartLine == PositionOf(Source, "function createUser").Line);
    }

    [Fact]
    public async Task FoldingRange_IncludesMultiLineDocComment()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fold-doc.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        FoldingRange[] ranges = await RequestFoldingAsync(session, uri);
        ranges.Should().Contain(range =>
            range.Kind == FoldingRangeKind.Comment
            && range.StartLine == PositionOf(Source, "/**").Line);
    }

    [Fact]
    public async Task FoldingRange_IncludesImportGroup()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fold-use.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        FoldingRange[] ranges = await RequestFoldingAsync(session, uri);
        ranges.Should().Contain(range =>
            range.Kind == FoldingRangeKind.Imports
            && range.StartLine == PositionOf(Source, "use App\\A").Line
            && range.EndLine == PositionOf(Source, "use App\\B").Line);
    }

    [Fact]
    public async Task FoldingRange_IncludesMultiLineArray()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fold-array.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        FoldingRange[] ranges = await RequestFoldingAsync(session, uri);
        ranges.Should().Contain(range =>
            range.Kind == FoldingRangeKind.Region
            && range.StartLine == PositionOf(Source, "$values = [").Line);
    }

    [Fact]
    public async Task FoldingRange_SingleLineFunction_IsNotFolded()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fold-oneline.tyhp");
        const string source = "<?tyhp\nfunction one(): int { return 1; }\n";
        await OpenAndWaitAsync(session, uri, source);

        FoldingRange[] ranges = await RequestFoldingAsync(session, uri);
        ranges.Should().NotContain(range =>
            range.Kind == FoldingRangeKind.Region
            && range.StartLine == PositionOf(source, "function one").Line);
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

    private static Task<FoldingRange[]> RequestFoldingAsync(LspTestSession session, Uri uri)
    {
        return session.Client.InvokeWithParameterObjectAsync<FoldingRange[]>(
            Methods.TextDocumentFoldingRangeName,
            new FoldingRangeParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });
    }

    private static Position PositionOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        return PositionUtilities.GetPosition(source, index);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-folding-tests", fileName);
        return new Uri(path);
    }
}
