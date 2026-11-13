using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Exceptions;
using Tyhp.LanguageServer.Analysis;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class StructInitializerLspTests
{
    [Fact]
    public async Task Completion_InNewStructInitializer_OffersUnsetFieldsRequiredFirst()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
                bool $ok = false;
            };

            function demo(): void {
                $p = new Point[
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "new Point[");
        string[] labels = Labels(list);
        labels.Should().Contain("x");
        labels.Should().Contain("y");
        labels.Should().Contain("ok");
        IndexOf(labels, "ok").Should().BeGreaterThan(IndexOf(labels, "x"));
        IndexOf(labels, "ok").Should().BeGreaterThan(IndexOf(labels, "y"));
        KindOf(list, "x").Should().Be(CompletionItemKind.Property);
        DetailOf(list, "x").Should().Contain("float");
        DetailOf(list, "x").Should().Contain("required");
        DetailOf(list, "y").Should().Contain("required");
        DetailOf(list, "ok").Should().Contain("bool");
        DetailOf(list, "ok").Should().Contain("false");
    }

    [Fact]
    public async Task Completion_InNewStructInitializer_OmitsKeysAlreadyPresent()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
                bool $ok = false;
            };

            function demo(): void {
                $p = new Point[
                    x => 1.0,
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "x => 1.0,");
        string[] labels = Labels(list);
        labels.Should().NotContain("x");
        labels.Should().Contain("y");
        labels.Should().Contain("ok");
        IndexOf(labels, "ok").Should().BeGreaterThan(IndexOf(labels, "y"));
    }

    [Fact]
    public async Task Completion_InNewWithList_OffersUnsetStructFields()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
                bool $ok = false;
            };

            function demo(): void {
                $p = new Point() with [
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "new Point() with [");
        string[] labels = Labels(list);
        labels.Should().Contain("x");
        labels.Should().Contain("y");
        labels.Should().Contain("ok");
        labels.Should().NotContain("Point");
        IndexOf(labels, "ok").Should().BeGreaterThan(IndexOf(labels, "x"));
    }

    [Fact]
    public async Task Completion_InVariableWithList_OffersUnsetStructFields()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
                bool $ok = false;
            };

            function demo(): void {
                Point $p = new Point[x => 1.0, y => 2.0, ok => true];
                $q = $p with [
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "$p with [");
        string[] labels = Labels(list);
        labels.Should().Contain("x");
        labels.Should().Contain("y");
        labels.Should().Contain("ok");
        IndexOf(labels, "ok").Should().BeGreaterThan(IndexOf(labels, "x"));
        IndexOf(labels, "ok").Should().BeGreaterThan(IndexOf(labels, "y"));
    }

    [Fact]
    public async Task Completion_InCloneWithList_OffersUnsetStructFields()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
                bool $ok = false;
            };

            function demo(): void {
                Point $p = new Point[x => 1.0, y => 2.0, ok => true];
                $q = clone $p with [
                    x => 3.0,
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "x => 3.0,");
        string[] labels = Labels(list);
        labels.Should().NotContain("x");
        labels.Should().Contain("y");
        labels.Should().Contain("ok");
    }

    [Fact]
    public async Task Completion_StructAlias_CompletesPropertyNameNotArrayKey()
    {
        const string source = """
            <?tyhp

            type Response = struct {
                int 'status_code' as $statusCode;
                string $body = "";
            };

            function demo(): void {
                $r = new Response[
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "new Response[");
        string[] labels = Labels(list);
        labels.Should().Contain("statusCode");
        labels.Should().NotContain("status_code");
        DetailOf(list, "statusCode").Should().Contain("int");
        DetailOf(list, "statusCode").Should().Contain("required");
        DetailOf(list, "body").Should().Contain("string");
        IndexOf(labels, "body").Should().BeGreaterThan(IndexOf(labels, "statusCode"));
    }

    [Fact]
    public async Task Hover_OnInitializerKey_ShowsTypeAndDefault()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                bool $ok = false;
            };

            function demo(): void {
                $p = new Point[
                    ok => true,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-struct-key.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await session.Client.InvokeWithParameterObjectAsync<Hover?>(
            Methods.TextDocumentHoverName,
            new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = PositionAt(source, "ok =>"),
            });
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("bool");
        text.Should().Contain("false");
        text.Should().Contain("$ok");
    }

    [Fact]
    public async Task CodeAction_NewWithOnStruct_RewritesToInitializerAndKeepsComment()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
            };

            function demo(): void {
                $p = new Point() with [
                    x => 1.0, // keep
                    y => 2.0,
                ];
            }
            """;

        string rewritten = await ApplyActionAsync(source, "new Point() with", "Use struct initializer");
        rewritten.Should().Contain("new Point[");
        rewritten.Should().Contain("// keep");
        rewritten.Should().NotContain("() with");
    }

    [Fact]
    public async Task CodeAction_GenericNewWith_RewritesToInitializer()
    {
        const string source = """
            <?tyhp

            type Box<T> = struct {
                T $value;
            };

            function demo(): void {
                $b = new Box<int>() with [
                    value => 1, // keep
                ];
            }
            """;

        string rewritten = await ApplyActionAsync(source, "new Box<int>() with", "Use struct initializer");
        rewritten.Should().Contain("new Box<int>[");
        rewritten.Should().Contain("// keep");
        rewritten.Should().NotContain("() with");
    }

    [Fact]
    public async Task CodeAction_AnonymousNewWith_RewritesToInitializer()
    {
        const string source = """
            <?tyhp

            function demo(): void {
                $p = new struct {
                    string $name;
                } with [
                    name => "a", // keep
                ];
            }
            """;

        string rewritten = await ApplyActionAsync(source, "} with", "Use struct initializer");
        rewritten.Should().Contain("}[");
        rewritten.Should().Contain("// keep");
        rewritten.Should().NotContain("with");
    }

    [Fact]
    public async Task CodeAction_BareNew_DoesNotOfferStructInitializer()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
            };

            function demo(): void {
                $p = new Point();
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("bare-new.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new Point()"));
        actions.Should().NotContain(action =>
            action.Title.Contains("struct initializer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CodeAction_NonStructInitializer_RewritesToWithList()
    {
        const string source = """
            <?tyhp

            class SomeClass {
                public string $name;
            }

            function demo(): void {
                $x = new SomeClass[
                    name => "a",
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("non-struct-initializer.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.CheckerStructInitializerOnNonStruct)));

        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new SomeClass["));
        CodeAction? fix = actions.FirstOrDefault(action =>
            action.Title.Contains("with", StringComparison.Ordinal));
        fix.Should().NotBeNull();
        fix!.Kind.Should().Be(CodeActionKind.QuickFix);
        string rewritten = ApplyFirstEdit(source, uri, fix.Edit!);
        rewritten.Should().Contain("new SomeClass() with [");
        rewritten.Should().Contain("name => \"a\"");
    }

    [Fact]
    public async Task CodeAction_TypeAliasInitializer_DoesNotRewriteToWithList()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
            };

            type P2 = Point;

            function demo(): void {
                $p = new P2[
                    x => 1.0,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("alias-initializer.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.CheckerStructInitializerOnNonStruct)));

        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new P2["));
        actions.Should().NotContain(action =>
            action.Kind == CodeActionKind.QuickFix
            && action.Title.Contains("with", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_InterfaceInitializer_DoesNotRewriteToWithList()
    {
        const string source = """
            <?tyhp

            interface Greeter {}

            function demo(): void {
                new Greeter[];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("interface-initializer.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.CheckerStructInitializerOnNonStruct)));

        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new Greeter["));
        actions.Should().NotContain(action => action.Kind == CodeActionKind.QuickFix);
    }

    [Fact]
    public async Task SemanticTokens_InitializerKey_IsProperty()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
            };

            function demo(): void {
                $p = new Point[
                    x => 1.0,
                    y => 2.0,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("struct-key-tokens.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        SemanticTokens tokens = await session.Client.InvokeWithParameterObjectAsync<SemanticTokens>(
            Methods.TextDocumentSemanticTokensFullName,
            new SemanticTokensParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });
        IReadOnlyList<DecodedSemanticToken> decoded = SemanticTokenCollector.Decode(tokens.Data ?? []);
        Position key = PositionAt(source, "x =>");
        decoded.Should().Contain(token =>
            token.Line == key.Line
            && token.Type == SemanticTokenTypes.Property
            && TokenText(source, token) == "x");
    }

    [Fact]
    public async Task Completion_SpreadInInitializer_OmitsFieldsTheSourceGuarantees()
    {
        const string source = """
            <?tyhp

            type Opts = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
                bool $debug = false;
                string $mode;
            };

            type Base = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
            };

            function demo(Base $base): void {
                $o = new Opts[
                    ...$base,
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "...$base,");
        string[] labels = Labels(list);
        labels.Should().NotContain("name");
        labels.Should().NotContain("retries");
        labels.Should().Contain("nick");
        labels.Should().Contain("debug");
        labels.Should().Contain("mode");
        IndexOf(labels, "mode").Should().BeLessThan(IndexOf(labels, "nick"));
    }

    [Fact]
    public async Task Completion_SpreadInWithList_OmitsFieldsTheSourceGuarantees()
    {
        const string source = """
            <?tyhp

            type Opts = struct {
                string $name;
                int $retries = 3;
                ?string $nick;
            };

            function demo(Opts $base, Opts $other): void {
                $o = $base with [
                    ...$other,
                ];
            }
            """;

        CompletionList list = await CompleteAsync(source, "...$other,");
        string[] labels = Labels(list);
        labels.Should().NotContain("name");
        labels.Should().NotContain("retries");
        labels.Should().Contain("nick");
    }

    [Fact]
    public async Task Hover_OnNestedArrayKeyInsideInitializerValue_IsNotAStructField()
    {
        const string source = """
            <?tyhp

            type Holder = struct {
                mixed $data;
                int $y;
            };

            function demo(): void {
                $h = new Holder[
                    data => [
                        'y' => 1,
                    ],
                    y => 2,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-nested-key.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await session.Client.InvokeWithParameterObjectAsync<Hover?>(
            Methods.TextDocumentHoverName,
            new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = PositionAt(source, "'y' =>"),
            });
        string text = hover is null ? string.Empty : HoverText(hover);
        text.Should().NotContain("$y");
        text.Should().NotContain("int");
    }

    [Fact]
    public async Task SemanticTokens_NestedArrayKeyAndClassName_AreNotProperties()
    {
        const string source = """
            <?tyhp

            type Holder = struct {
                mixed $data;
                int $y;
            };

            function demo(): void {
                $h = new Holder[
                    data => [
                        'y' => 1,
                    ],
                    y => 2,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("struct-nested-tokens.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        SemanticTokens tokens = await session.Client.InvokeWithParameterObjectAsync<SemanticTokens>(
            Methods.TextDocumentSemanticTokensFullName,
            new SemanticTokensParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });
        IReadOnlyList<DecodedSemanticToken> decoded = SemanticTokenCollector.Decode(tokens.Data ?? []);
        Position nested = PositionAt(source, "'y' =>");
        Position className = PositionAt(source, "Holder[");
        Position outerKey = PositionAt(source, "    y => 2");

        decoded.Should().NotContain(token =>
            token.Line == nested.Line && token.Type == SemanticTokenTypes.Property);
        decoded.Should().NotContain(token =>
            token.Line == className.Line
            && token.Type == SemanticTokenTypes.Property
            && TokenText(source, token) == "Holder");
        decoded.Where(token =>
                token.Line == outerKey.Line
                && token.Type == SemanticTokenTypes.Property
                && TokenText(source, token) == "y")
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task CodeAction_NonStructNewWith_DoesNotOfferStructInitializer()
    {
        const string source = """
            <?tyhp

            class Box {
                public int $value = 0;
            }

            function demo(): void {
                $b = new Box() with [
                    value => 1,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("class-new-with.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new Box() with"));
        actions.Should().NotContain(action =>
            action.Title.Contains("struct initializer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CodeAction_ChainedWith_DoesNotRewriteInnerListToInitializer()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
            };

            function demo(): void {
                $p = new Point() with [x => 1.0, y => 2.0] with [x => 3.0];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("chained-with.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new Point() with"));
        actions.Should().NotContain(action =>
            action.Title.Contains("struct initializer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CodeAction_GenericNonStructInitializer_KeepsTypeArguments()
    {
        const string source = """
            <?tyhp

            class Crate<T> {
                public mixed $item;
            }

            function demo(): void {
                $c = new Crate<int>[
                    item => 1,
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("generic-non-struct-initializer.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.CheckerStructInitializerOnNonStruct)));

        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new Crate<int>["));
        CodeAction? fix = actions.FirstOrDefault(action => action.Kind == CodeActionKind.QuickFix);
        fix.Should().NotBeNull();
        string rewritten = ApplyFirstEdit(source, uri, fix!.Edit!);
        rewritten.Should().Contain("new Crate<int>() with [");
    }

    [Fact]
    public async Task CodeAction_NonStructInitializerWithNestedStructInitializer_RewritesOnlyTheOuter()
    {
        const string source = """
            <?tyhp

            type Point = struct {
                float $x;
                float $y;
            };

            class Holder {
                public mixed $point;
            }

            function demo(): void {
                $h = new Holder[
                    point => new Point[x => 1.0, y => 2.0],
                ];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("nested-initializer.tyhp");
        await OpenAndWaitAsync(session, uri, source);
        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.CheckerStructInitializerOnNonStruct)));

        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, "new Holder["));
        CodeAction? fix = actions.FirstOrDefault(action => action.Kind == CodeActionKind.QuickFix);
        fix.Should().NotBeNull();
        string rewritten = ApplyFirstEdit(source, uri, fix!.Edit!);
        rewritten.Should().Contain("new Holder() with [");
        rewritten.Should().Contain("new Point[x => 1.0, y => 2.0]");
    }

    private static async Task<CompletionList> CompleteAsync(string source, string needle)
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("complete-" + Guid.NewGuid().ToString("N") + ".tyhp");
        await OpenAndWaitAsync(session, uri, source);
        return await session.Client.InvokeWithParameterObjectAsync<CompletionList>(
            Methods.TextDocumentCompletionName,
            new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = PositionAfter(source, needle),
            });
    }

    private static async Task<string> ApplyActionAsync(string source, string rangeNeedle, string titlePart)
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("action-" + Guid.NewGuid().ToString("N") + ".tyhp");
        await OpenAndWaitAsync(session, uri, source);
        CodeAction[] actions = await RequestCodeActionsAsync(session, uri, RangeOf(source, rangeNeedle));
        CodeAction? action = actions.FirstOrDefault(item =>
            item.Title.Contains(titlePart, StringComparison.OrdinalIgnoreCase));
        action.Should().NotBeNull(
            "actions were [{0}]",
            string.Join(" | ", actions.Select(item => item.Title)));
        action!.Edit.Should().NotBeNull();
        return ApplyFirstEdit(source, uri, action.Edit!);
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

    private static Task<CodeAction[]> RequestCodeActionsAsync(LspTestSession session, Uri uri, ProtocolRange range)
    {
        return session.Client.InvokeWithParameterObjectAsync<CodeAction[]>(
            Methods.TextDocumentCodeActionName,
            new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Range = range,
                Context = new CodeActionContext { Diagnostics = [] },
            });
    }

    private static Position PositionAfter(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        return PositionUtilities.GetPosition(source, index + needle.Length);
    }

    private static Position PositionAt(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        return PositionUtilities.GetPosition(source, index);
    }

    private static ProtocolRange RangeOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        return new ProtocolRange
        {
            Start = PositionUtilities.GetPosition(source, index),
            End = PositionUtilities.GetPosition(source, index + needle.Length),
        };
    }

    private static string[] Labels(CompletionList list)
        => list.Items.Select(item => item.Label).ToArray();

    private static int IndexOf(string[] labels, string name)
    {
        int index = Array.IndexOf(labels, name);
        index.Should().BeGreaterThanOrEqualTo(0, $"completion should offer '{name}'");
        return index;
    }

    private static string DetailOf(CompletionList list, string label)
        => list.Items.First(item => item.Label == label).Detail ?? string.Empty;

    private static CompletionItemKind? KindOf(CompletionList list, string label)
        => list.Items.First(item => item.Label == label).Kind;

    private static string ApplyFirstEdit(string source, Uri uri, WorkspaceEdit edit)
    {
        edit.Changes.Should().NotBeNull();
        string key = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
        TextEdit[]? edits = null;
        foreach (KeyValuePair<string, TextEdit[]> pair in edit.Changes!)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal)
                || pair.Key.EndsWith(uri.AbsolutePath, StringComparison.Ordinal))
            {
                edits = pair.Value;
                break;
            }
        }

        edits.Should().NotBeNull();
        TextEdit first = edits![0];
        int start = PositionUtilities.GetOffset(source, first.Range.Start);
        int end = PositionUtilities.GetOffset(source, first.Range.End);
        return source[..start] + first.NewText + source[end..];
    }

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

    private static string TokenText(string source, DecodedSemanticToken token)
    {
        Position start = new() { Line = token.Line, Character = token.Character };
        int offset = PositionUtilities.GetOffset(source, start);
        int end = Math.Min(source.Length, offset + token.Length);
        return offset >= 0 && offset < end ? source[offset..end] : string.Empty;
    }

    private static bool IsCode(LspDiagnostic diagnostic, MessageCode expected)
    {
        object? code = diagnostic.Code;
        int want = (int)expected;
        if (code is int i)
        {
            return i == want;
        }

        if (code is SumType<int, string> sum)
        {
            return sum.Value is int si && si == want
                || sum.Value is string s && int.TryParse(s, out int parsed) && parsed == want;
        }

        return code is string text && int.TryParse(text, out int n) && n == want;
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-struct-initializer-lsp", fileName);
        return new Uri(path);
    }
}
