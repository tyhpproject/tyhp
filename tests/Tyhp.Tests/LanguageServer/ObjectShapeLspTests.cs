using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
[Trait("Category", "Story27")]
public class ObjectShapeLspTests
{
    private const string Source = """
        <?tyhp

        type ClockShape = object {
            public string $timezone;
            public function now(): string;
            public function __construct(string $tz): void;
        };

        function formatNow(ClockShape $c): string {
            return $c->now();
        }

        function takeClock(): 
        function makeClock(): object {
            mixed $x = new 
        }

        function staticClock(): void {
            ClockShape::
        }
        """;

    [Fact]
    public async Task Hover_OnClockShape_DescribesObjectShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-hover.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "ClockShape ="));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("object shape");
        text.Should().Contain("```tyhp");
        text.Should().Contain("type ClockShape = object {");
        text.Should().Contain("function now");
        text.Should().Contain("string $timezone");
    }

    [Fact]
    public async Task Hover_OnClockShapeParameterType_DescribesObjectShape()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-hover-param.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "(ClockShape $c)"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("object shape");
        text.Should().Contain("object {");
        text.Should().Contain("function now");
    }

    [Fact]
    public async Task Completion_AfterArrowOnShape_ListsInstanceMembersNotConstruct()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-arrow.tyhp");
        const string source = """
            <?tyhp
            type ClockShape = object {
                public string $timezone;
                public function now(): string;
                public function __construct(string $tz): void;
            };
            function formatNow(ClockShape $c): string {
                return $c->
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$c->"));
        string[] labels = Labels(list);
        labels.Should().Contain("now");
        labels.Should().Contain("timezone");
        labels.Should().NotContain("__construct");
        KindOf(list, "now").Should().Be(CompletionItemKind.Method);
        KindOf(list, "timezone").Should().Be(CompletionItemKind.Property);
    }

    [Fact]
    public async Task Completion_AfterNew_DoesNotOfferShapeAlias()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-new.tyhp");
        const string source = """
            <?tyhp
            type ClockShape = object {
                public function now(): string;
            };
            class Clock {
                public function now(): string { return ""; }
            }
            function make(): object {
                mixed $x = new 
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "new "));
        string[] labels = Labels(list);
        labels.Should().Contain("Clock");
        labels.Should().NotContain("ClockShape");
        list.Items.Should().NotContain(item =>
            (item.InsertText ?? item.Label).Contains("new ClockShape", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InTypeAnnotation_OffersShapeAlias()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-type.tyhp");
        const string source = """
            <?tyhp
            type ClockShape = object {
                public function now(): string;
            };
            function takeClock(): 
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionAfter(source, "function takeClock(): "));
        Labels(list).Should().Contain("ClockShape");
    }

    [Fact]
    public async Task Completion_AfterDoubleColonOnShape_DoesNotOfferMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-static.tyhp");
        const string source = """
            <?tyhp
            type ClockShape = object {
                public function now(): string;
                public function __construct(string $tz): void;
            };
            function staticClock(): void {
                ClockShape::
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "ClockShape::"));
        string[] labels = Labels(list);
        labels.Should().NotContain("now");
        labels.Should().NotContain("__construct");
        labels.Should().NotContain("timezone");
    }

    [Fact]
    public async Task Definition_OnClockShape_JumpsToTypeAlias()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-definition.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(Source, "(ClockShape $c)"));
        locations.Should().NotBeEmpty();
        SameUri(locations[0].Uri, uri).Should().BeTrue();
        locations[0].Range.Start.Line.Should().Be(PositionOf(Source, "ClockShape =").Line);
    }

    [Fact]
    public async Task Definition_OnShapeMethod_JumpsToShapeMember()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-definition-method.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(Source, "$c->now()"));
        locations.Should().NotBeEmpty();
        SameUri(locations[0].Uri, uri).Should().BeTrue();
        locations[0].Range.Start.Line.Should().Be(PositionOf(Source, "function now()").Line);
    }

    [Fact]
    public async Task Completion_AfterArrowOnIntersectionShape_ListsNominalAndShapeMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-intersection.tyhp");
        const string source = """
            <?tyhp
            interface Logger {
                public function info(string $m): void;
            }
            type TimestampedLogger = Logger & object {
                public function getLastLogAt(): int;
            };
            function take(TimestampedLogger $l): void {
                $l->
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$l->"));
        string[] labels = Labels(list);
        labels.Should().Contain("getLastLogAt");
        labels.Should().Contain("info");
    }

    [Fact]
    public async Task Completion_AfterArrowOnGenericShapeParameter_ListsShapeMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-generic.tyhp");
        const string source = """
            <?tyhp
            type Box<T> = object {
                public function get(): T;
            };
            function take(Box<int> $b): void {
                $b->
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$b->"));
        string[] labels = Labels(list);
        labels.Should().Contain("get");
    }

    [Fact]
    public async Task Completion_AfterArrowOnGenericShapeParameter_CompleteStatement_ListsShapeMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("shape-completion-generic-complete.tyhp");
        const string source = """
            <?tyhp
            type Box<T> = object {
                public function get(): T;
            };
            function take(Box<int> $b): void {
                $b->get();
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$b->"));
        string[] labels = Labels(list);
        labels.Should().Contain("get");
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

    private static Task<Location[]> RequestDefinitionAsync(LspTestSession session, Uri uri, Position position)
    {
        return session.Client.InvokeWithParameterObjectAsync<Location[]>(
            Methods.TextDocumentDefinitionName,
            new TextDocumentPositionParams
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
        if (needle.Contains("->", StringComparison.Ordinal))
        {
            offset = index + needle.LastIndexOf("->", StringComparison.Ordinal) + 2;
        }
        else if (needle.EndsWith("->", StringComparison.Ordinal)
            || needle.EndsWith("::", StringComparison.Ordinal)
            || needle.EndsWith("new ", StringComparison.Ordinal))
        {
            offset = index + needle.Length;
        }
        else if (needle.StartsWith("(", StringComparison.Ordinal)
            || needle.StartsWith("function ", StringComparison.Ordinal)
            || needle.StartsWith(": ", StringComparison.Ordinal))
        {
            int firstIdent = needle.IndexOfAny(['C', 'n', 't']);
            if (needle.Contains("ClockShape", StringComparison.Ordinal))
            {
                offset = index + needle.IndexOf("ClockShape", StringComparison.Ordinal);
            }
            else if (firstIdent >= 0)
            {
                offset = index + firstIdent;
            }
        }

        if (needle.EndsWith("->", StringComparison.Ordinal) || needle.EndsWith("::", StringComparison.Ordinal) || needle == "new ")
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

    private static CompletionItemKind? KindOf(CompletionList list, string label)
        => list.Items.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal))?.Kind;

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
        string path = Path.Combine(Path.GetTempPath(), "tyhp-object-shape-lsp-tests", fileName);
        return new Uri(path);
    }

    private static bool SameUri(Uri? left, Uri right)
    {
        if (left is null)
        {
            return false;
        }

        string a = left.IsAbsoluteUri ? left.AbsoluteUri : left.ToString();
        string b = right.IsAbsoluteUri ? right.AbsoluteUri : right.ToString();
        return string.Equals(a, b, StringComparison.Ordinal);
    }
}
