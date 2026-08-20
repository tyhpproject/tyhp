using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class FormattingHandlerTests
{
    [Fact]
    public void SortImports_OrdersClassesAlphabetically()
    {
        const string source = """
            <?tyhp
            use Zzz\A;
            use Aaa\B;
            class Demo {}
            """;
        string sorted = UseStatementEdits.SortImports(source);
        int aaa = sorted.IndexOf("use Aaa\\B;", StringComparison.Ordinal);
        int zzz = sorted.IndexOf("use Zzz\\A;", StringComparison.Ordinal);
        aaa.Should().BeGreaterThanOrEqualTo(0);
        zzz.Should().BeGreaterThan(aaa);
    }

    [Fact]
    public void SortImports_GroupsClassesFunctionsConstants()
    {
        const string source = """
            <?tyhp
            use const App\Z;
            use function App\b;
            use App\A;
            class Demo {}
            """;
        string sorted = UseStatementEdits.SortImports(source);
        int cls = sorted.IndexOf("use App\\A;", StringComparison.Ordinal);
        int fn = sorted.IndexOf("use function App\\b;", StringComparison.Ordinal);
        int cnst = sorted.IndexOf("use const App\\Z;", StringComparison.Ordinal);
        cls.Should().BeLessThan(fn);
        fn.Should().BeLessThan(cnst);
    }

    [Fact]
    public void Format_NormalizesIndentationToSpaces()
    {
        const string source = """
            <?tyhp
            class Foo {
            public function bar(): void {
            return;
            }
            }
            """;
        TextEdit[] edits = DocumentFormatter.Format(
            source,
            new FormattingOptions { TabSize = 4, InsertSpaces = true },
            range: null);
        edits.Should().NotBeEmpty();
        string formatted = Apply(source, edits);
        formatted.Should().Contain("    public function bar(): void {");
        formatted.Should().Contain("        return;");
    }

    [Fact]
    public async Task Formatting_Document_SortsImportsAndIndents()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fmt.tyhp");
        const string source = """
            <?tyhp
            use Zzz\A;
            use Aaa\B;
            class Foo {
            public function bar(): void {
            return;
            }
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        TextEdit[] edits = await session.Client.InvokeWithParameterObjectAsync<TextEdit[]>(
            Methods.TextDocumentFormattingName,
            new DocumentFormattingParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Options = new FormattingOptions { TabSize = 4, InsertSpaces = true },
            });
        string formatted = Apply(source, edits);
        formatted.IndexOf("use Aaa\\B;", StringComparison.Ordinal)
            .Should().BeLessThan(formatted.IndexOf("use Zzz\\A;", StringComparison.Ordinal));
        formatted.Should().Contain("    public function bar(): void {");
    }

    [Fact]
    public async Task RangeFormatting_OnlyChangesSelectedLines()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fmt-range.tyhp");
        const string source = """
            <?tyhp
            use Zzz\A;
            use Aaa\B;
            class Foo {
            public function bar(): void {
            return;
            }
            }
            """;
        await OpenAndWaitAsync(session, uri, source);
        ProtocolRange range = new()
        {
            Start = PositionOf(source, "public function bar"),
            End = PositionOf(source, "return;"),
        };
        range.End = new Position { Line = range.End.Line, Character = range.End.Character + "return;".Length };

        TextEdit[] edits = await session.Client.InvokeWithParameterObjectAsync<TextEdit[]>(
            Methods.TextDocumentRangeFormattingName,
            new DocumentRangeFormattingParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Range = range,
                Options = new FormattingOptions { TabSize = 4, InsertSpaces = true },
            });
        string formatted = Apply(source, edits);
        formatted.Should().Contain("use Zzz\\A;");
        int zzz = formatted.IndexOf("use Zzz\\A;", StringComparison.Ordinal);
        int aaa = formatted.IndexOf("use Aaa\\B;", StringComparison.Ordinal);
        zzz.Should().BeLessThan(aaa);
        formatted.Should().Contain("    public function bar(): void {");
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
        index.Should().BeGreaterThanOrEqualTo(0);
        return PositionUtilities.GetPosition(source, index);
    }

    private static string Apply(string source, TextEdit[] edits)
    {
        if (edits.Length == 0)
        {
            return source;
        }

        var ordered = edits
            .OrderByDescending(e => e.Range.Start.Line)
            .ThenByDescending(e => e.Range.Start.Character)
            .ToList();
        string result = source;
        foreach (TextEdit edit in ordered)
        {
            int start = PositionUtilities.GetOffset(result, edit.Range.Start);
            int end = PositionUtilities.GetOffset(result, edit.Range.End);
            result = result[..start] + edit.NewText + result[end..];
        }

        return result;
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-formatting-tests", fileName);
        return new Uri(path);
    }
}
