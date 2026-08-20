using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class HoverHandlerTests
{
    private const string Source = """
        <?tyhp

        /**
         * A user record.
         */
        class User {
            public string $name;

            /**
             * @deprecated Use display() instead
             */
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
    public async Task Hover_OnVariable_ShowsInferredOrDeclaredType()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-var.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "return $user"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("```tyhp");
        text.Should().Contain("User");
        text.Should().Contain("$user");
    }

    [Fact]
    public async Task Hover_OnMethodName_ShowsSignature()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-method.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "function greet"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("function greet");
        text.Should().Contain("string");
        text.Should().Contain("display()");
    }

    [Fact]
    public async Task Hover_OnClassName_ShowsClassSignatureAndDocComment()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-class.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "class User"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("class");
        text.Should().Contain("User");
        text.Should().Contain("A user record.");
    }

    [Fact]
    public async Task Hover_OnAssignmentTarget_ShowsRightHandSideInferredType()
    {
        // The checker's InferAssignment only calls InferExpressionType on the right-hand side
        // of a plain `=` (see TypeInferrer.Expressions.cs); the left-hand variable node itself
        // never gets a fresh dictionary entry. Without SymbolFinder redirecting to the
        // right-hand side, hovering "$user" here shows the checker's "unresolved" sentinel
        // instead of "User".
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-assign-target.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(Source, "$user = new User()"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("User");
        text.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnForeachValue_ShowsIterableElementType()
    {
        const string source = """
            <?tyhp

            class Item {}

            function collect(array<Item> $items): void {
                array<Item> $copy = [];
                foreach ($items as $item) {
                    $copy[] = $item;
                }
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-foreach.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? onBinding = await RequestHoverAsync(session, uri, PositionOf(source, "as $item"));
        onBinding.Should().NotBeNull();
        string bindingText = HoverText(onBinding!);
        bindingText.Should().Contain("Item");
        bindingText.Should().Contain("$item");
        bindingText.Should().NotContain("unresolved");

        Hover? onUse = await RequestHoverAsync(session, uri, PositionOf(source, "= $item"));
        onUse.Should().NotBeNull();
        string useText = HoverText(onUse!);
        useText.Should().Contain("Item");
        useText.Should().Contain("$item");
        useText.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnNestedForeachValue_ShowsInnerElementType()
    {
        const string source = """
            <?tyhp

            function unique(array<string> $values): void {
                array<string> $seen = [];
                foreach ($values as $value) {
                    foreach ($seen as $existing) {
                        if ($value === $existing) {
                            continue 2;
                        }
                    }
                    $seen[] = $value;
                }
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-foreach-nested.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? existing = await RequestHoverAsync(session, uri, PositionOf(source, "=== $existing"));
        existing.Should().NotBeNull();
        string text = HoverText(existing!);
        text.Should().Contain("string");
        text.Should().Contain("$existing");
        text.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnGenericArrayProperty_ShowsTypeArguments()
    {
        const string source = """
            <?tyhp

            class Type {
                private static array<string, self> $singletons = [];
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-array-generic.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(source, "$singletons"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("array<string, self>");
        text.Should().Contain("$singletons");
        text.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnWhitespace_ReturnsNull()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-ws.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Hover? hover = await RequestHoverAsync(session, uri, new Position { Line = 1, Character = 0 });
        hover.Should().BeNull();
    }

    [Fact]
    public async Task Hover_OnNarrowedParameter_ShowsNarrowedTypeAndDeclaredType()
    {
        const string source = """
            <?tyhp

            function greet(?string $name): string {
                if ($name === null) {
                    return "";
                }
                return $name;
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-narrowed-param.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(source, "return $name"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("```tyhp");
        text.Should().Contain("string $name");
        text.Should().Contain("declared `?string`");
        text.Should().NotContain("?string $name");
        text.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnArrowCapturedNarrowedParameter_ShowsNarrowedType()
    {
        const string source = """
            <?tyhp

            function wrap(?string $value): string {
                if ($value === null) {
                    return "";
                }
                return (static fn(): string => $value)();
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-arrow-narrowed.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(source, "=> $value"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("string $value");
        text.Should().Contain("declared `?string`");
        text.Should().NotContain("?string $value");
        text.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnNarrowedParameterPassedToConstructor_ShowsNarrowedType()
    {
        const string source = """
            <?tyhp

            class Box<T> {
                public function __construct(callable<T> $executor) {}
            }

            function wrap(?string $value): Box<string> {
                if ($value === null) {
                    return new Box<string>(static fn(): string => "");
                }
                return new Box<string>(static fn(): string => $value);
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-ctor-narrowed.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(source, "=> $value"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("string $value");
        text.Should().Contain("declared `?string`");
        text.Should().NotContain("?string $value");
        text.Should().NotContain("unresolved");
    }

    [Fact]
    public async Task Hover_OnParameterDeclaration_ShowsDeclaredNullableType()
    {
        const string source = """
            <?tyhp

            function greet(?string $name): string {
                if ($name === null) {
                    return "";
                }
                return $name;
            }
            """;

        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("hover-param-decl.tyhp");
        await OpenAndWaitAsync(session, uri, source);

        Hover? hover = await RequestHoverAsync(session, uri, PositionOf(source, "?string $name"));
        hover.Should().NotBeNull();
        string text = HoverText(hover!);
        text.Should().Contain("?string $name");
        text.Should().NotContain("declared `");
        text.Should().NotContain("unresolved");
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

    private static Position PositionOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist in source");
        int offset = index;
        int dollar = needle.IndexOf('$');
        if (dollar >= 0)
        {
            offset = index + dollar;
            return PositionUtilities.GetPosition(source, offset);
        }

        if (needle.StartsWith("return ", StringComparison.Ordinal)
            || needle.StartsWith("class ", StringComparison.Ordinal)
            || needle.StartsWith("function ", StringComparison.Ordinal)
            || needle.StartsWith("as ", StringComparison.Ordinal)
            || needle.StartsWith("= ", StringComparison.Ordinal)
            || needle.StartsWith("=== ", StringComparison.Ordinal)
            || needle.StartsWith("=> ", StringComparison.Ordinal))
        {
            int lastSpace = needle.LastIndexOf(' ');
            if (lastSpace >= 0)
            {
                offset = index + lastSpace + 1;
            }
        }

        return PositionUtilities.GetPosition(source, offset);
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

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-hover-tests", fileName);
        return new Uri(path);
    }
}
