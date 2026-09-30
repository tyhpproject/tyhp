using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
[Trait("Category", "Story27.1")]
public class CallableStructShapeLspTests
{
    private const string Source = """
        <?tyhp

        type Mapper = callable(int $i): string;
        type Point = struct {
            float $x;
            float $y;
        };

        function map(Mapper $m): string {
            return $m(1);
        }

        function origin(): Point {
            return new Point() with [x => 0.0, y => 0.0];
        }

        function apply(callable(int $i): string $cb): string {
            return $cb(1);
        }

        function make(): mixed {
            mixed $x = new 
        }
        """;

    [Fact]
    public async Task Hover_OnInlineCallableShape_DescribesCallableShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("callable-hover-inline.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "callable(int $i): string $cb"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("callable shape");
        text.Should().Contain("```tyhp");
        text.Should().Contain("callable(int $i): string");
    }

    [Fact]
    public async Task Hover_OnMapperAlias_DescribesCallableShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("callable-hover-alias.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "Mapper ="));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("callable shape");
        text.Should().Contain("```tyhp");
        text.Should().Contain("type Mapper = callable(int $i): string");
    }

    [Fact]
    public async Task Hover_OnMapperParameterType_DescribesCallableShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("callable-hover-param.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "(Mapper $m)"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("callable shape");
        text.Should().Contain("callable(int $i): string");
    }

    [Fact]
    public async Task Hover_OnPointAlias_DescribesStructShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("struct-hover-alias.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "Point ="));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("struct shape");
        text.Should().Contain("```tyhp");
        text.Should().Contain("type Point = struct {");
        text.Should().Contain("float $x");
        text.Should().Contain("float $y");
    }

    [Fact]
    public async Task Hover_OnPointUseSite_DescribesStructShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("struct-hover-use.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "(): Point"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("struct shape");
        text.Should().Contain("struct {");
        text.Should().Contain("float $x");
    }

    [Fact]
    public async Task Completion_AfterNew_OffersPointNotMapper()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-new.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(Source, "new "));
        string[] labels = Labels(list);
        labels.Should().Contain("Point");
        labels.Should().NotContain("Mapper");
        list.Items.Should().NotContain(item =>
            (item.InsertText ?? item.Label).Contains("new Mapper", StringComparison.Ordinal));
        list.Items.Should().Contain(item =>
            string.Equals(item.Label, "Point", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InTypeAnnotation_OffersMapperAndPoint()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-type.tyhp");
        const string source = """
            <?tyhp
            type Mapper = callable(int $i): string;
            type Point = struct {
                float $x;
                float $y;
            };
            function take(): 
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionAfter(source, "function take(): "));
        string[] labels = Labels(list);
        labels.Should().Contain("Mapper");
        labels.Should().Contain("Point");
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

    private static Task<Hover?> RequestHoverAsync(LspTestSession session, Uri uri, Position position)
    {
        return session.Client.InvokeWithParameterObjectAsync<Hover?>(
            Methods.TextDocumentHoverName,
            new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = position,
            });
    }

    private static Task<CompletionList> RequestCompletionAsync(LspTestSession session, Uri uri, Position position)
    {
        return session.Client.InvokeWithParameterObjectAsync<CompletionList>(
            Methods.TextDocumentCompletionName,
            new CompletionParams
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
        if (needle.Contains("Mapper", StringComparison.Ordinal))
        {
            offset = index + needle.IndexOf("Mapper", StringComparison.Ordinal);
        }
        else if (needle.Contains("Point", StringComparison.Ordinal))
        {
            offset = index + needle.IndexOf("Point", StringComparison.Ordinal);
        }

        if (needle.EndsWith("new ", StringComparison.Ordinal) || needle == "new ")
        {
            offset = index + needle.Length;
        }

        return PositionUtilities.GetPosition(source, offset);
    }

    private static Position PositionAfter(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist in source");
        return PositionUtilities.GetPosition(source, index + needle.Length);
    }

    private static string[] Labels(CompletionList list)
        => list.Items.Select(item => item.Label).ToArray();

    private static string HoverText(Hover hover)
    {
        if (hover.Contents.TryGetThird(out MarkupContent? markup)
            && markup is not null
            && !string.IsNullOrEmpty(markup.Value))
        {
            return markup.Value;
        }

        return hover.Contents.Value switch
        {
            MarkupContent inner => inner.Value ?? string.Empty,
            string text => text,
            MarkedString marked => marked.Value ?? string.Empty,
            _ => hover.Contents.Value?.ToString() ?? string.Empty,
        };
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-callable-struct-shape-lsp-tests", fileName);
        return new Uri(path);
    }
}
