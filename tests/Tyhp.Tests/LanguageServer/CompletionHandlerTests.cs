using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class CompletionHandlerTests
{
    private const string Source = """
        <?tyhp

        /**
         * A user record.
         */
        class User {
            public string $name;
            public int $age;

            /**
             * Greets the user.
             */
            public function greet(): string {
                return "Hi, I'm " . $this->name;
            }

            public static function create(): User {
                return new User();
            }
        }

        function testCompletion(): void {
            $user = new User();
            $user->
        }
        """;

    [Fact]
    public async Task Completion_AfterArrow_ReturnsInstanceMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-arrow.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(Source, "$user->"));
        string[] labels = Labels(list);
        labels.Should().Contain("name");
        labels.Should().Contain("age");
        labels.Should().Contain("greet");
        KindOf(list, "name").Should().Be(CompletionItemKind.Property);
        KindOf(list, "greet").Should().Be(CompletionItemKind.Method);
    }

    [Fact]
    public async Task Completion_AfterNullSafeArrow_ReturnsInstanceMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-nullsafe-arrow.tyhp");
        const string source = """
            <?tyhp
            class User {
                public string $name;
            }
            function testCompletion(): void {
                $user = new User();
                $user?->
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$user?->"));
        Labels(list).Should().Contain("name");
        KindOf(list, "name").Should().Be(CompletionItemKind.Property);
    }

    [Fact]
    public async Task Completion_AfterArrow_HidesPrivateMembersInheritedFromParent()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-private-inherited.tyhp");
        const string source = """
            <?tyhp
            class Base {
                private function secret(): string { return ""; }
                protected function shared(): string { return ""; }
            }
            class Child extends Base {
                public function test(): void {
                    $this->
                }
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$this->"));
        Labels(list).Should().NotContain("secret");
        Labels(list).Should().Contain("shared");
    }

    [Fact]
    public async Task Completion_AfterArrow_HidesPrivateMembersOfOtherInstance()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-private-other-instance.tyhp");
        const string source = """
            <?tyhp
            class Base {
                private function secret(): string { return ""; }
            }
            function test(): void {
                $base = new Base();
                $base->
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$base->"));
        Labels(list).Should().NotContain("secret");
    }

    [Fact]
    public async Task Completion_AfterDollar_ReturnsInScopeVariables()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-var.tyhp");
        const string source = """
            <?tyhp
            class User {}
            function testCompletion(): void {
                $user = new User();
                $u
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$u\n"));
        Labels(list).Should().Contain("$user");
        KindOf(list, "$user").Should().Be(CompletionItemKind.Variable);
    }

    [Fact]
    public async Task Completion_InTypeAnnotation_ReturnsTypes()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-type.tyhp");
        const string source = """
            <?tyhp
            class User {}
            function createUser(): 
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionAfter(source, "function createUser(): "));
        string[] labels = Labels(list);
        labels.Should().Contain("User");
        labels.Should().Contain("string");
        labels.Should().Contain("int");
        KindOf(list, "User").Should().Be(CompletionItemKind.Class);
    }

    [Fact]
    public async Task Completion_AfterDoubleColon_ReturnsStaticMembers()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-static.tyhp");
        const string source = """
            <?tyhp
            class User {
                public static function create(): User {
                    return new User();
                }
            }
            function test(): void {
                User::
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "User::"));
        Labels(list).Should().Contain("create");
        KindOf(list, "create").Should().Be(CompletionItemKind.Method);
    }

    [Fact]
    public async Task Completion_AfterBackslash_ReturnsNamespaceSegments()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri modelsUri = FileUri("App/Models/User.tyhp");
        Uri mainUri = FileUri("main-ns.tyhp");
        const string models = "<?tyhp\nnamespace App\\Models;\nclass User {}\n";
        const string main = "<?tyhp\nnamespace App;\nfunction go(): void {\n    \\App\\\n}\n";
        await OpenAndWaitAsync(session, modelsUri, models);
        await OpenAndWaitAsync(session, mainUri, main);

        CompletionList list = await RequestCompletionAsync(session, mainUri, PositionOf(main, "\\App\\"));
        Labels(list).Should().Contain("Models");
    }

    [Fact]
    public async Task Completion_AutoImport_AddsUseStatementEdit()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri modelsUri = FileUri("Models/User.tyhp");
        Uri mainUri = FileUri("Controllers/main.tyhp");
        const string models = "<?tyhp\nnamespace App\\Models;\nclass User {}\n";
        const string main = """
            <?tyhp
            namespace App\Controllers;
            function getUser(): 
            """;
        await OpenAndWaitAsync(session, modelsUri, models);
        await OpenAndWaitAsync(session, mainUri, main);

        CompletionList list = await RequestCompletionAsync(session, mainUri, PositionAfter(main, "function getUser(): "));
        CompletionItem? user = ItemNamed(list, "User");
        user.Should().NotBeNull();
        user!.AdditionalTextEdits.Should().NotBeNull();
        user.AdditionalTextEdits.Should().Contain(edit =>
            edit.NewText.Contains("use App\\Models\\User;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_DeprecatedMember_IsMarkedDeprecated()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-deprecated.tyhp");
        const string source = """
            <?tyhp
            class User {
                /**
                 * @deprecated Use display() instead
                 */
                public function greet(): string { return ""; }
            }
            function test(): void {
                $user = new User();
                $user->
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(source, "$user->"));
        CompletionItem? greet = ItemNamed(list, "greet");
        greet.Should().NotBeNull();
        greet!.Detail.Should().Contain("deprecated");
        DocumentationText(greet).Should().Contain("Deprecated");
    }

    [Fact]
    public async Task Completion_IncludesDocCommentOnMember()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-doc.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(Source, "$user->"));
        CompletionItem? greet = ItemNamed(list, "greet");
        greet.Should().NotBeNull();
        DocumentationText(greet!).Should().Contain("Greets the user.");
    }

    [Fact]
    public async Task Completion_UnknownDocument_ReturnsEmptyList()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();

        CompletionList list = await RequestCompletionAsync(
            session,
            FileUri("missing.tyhp"),
            new Position { Line = 0, Character = 0 });
        list.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CompletionResolve_FillsDocumentationFromData()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("completion-resolve.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        CompletionList list = await RequestCompletionAsync(session, uri, PositionOf(Source, "$user->"));
        CompletionItem greet = ItemNamed(list, "greet")!;
        greet.Documentation = default;
        CompletionItem resolved = await session.Client.InvokeWithParameterObjectAsync<CompletionItem>(
            Methods.TextDocumentCompletionResolveName,
            greet);
        DocumentationText(resolved).Should().Contain("Greets the user.");
    }

    [Fact]
    public void Detect_Arrow_IsInstanceMember()
    {
        const string source = "$user->";
        CompletionEngine.Detect(source, source.Length, context: null, ast: null)
            .Should().Be(CompletionContextKind.InstanceMember);
    }

    [Fact]
    public void Detect_Dollar_IsVariable()
    {
        const string source = "$";
        CompletionEngine.Detect(source, source.Length, context: null, ast: null)
            .Should().Be(CompletionContextKind.Variable);
    }

    [Fact]
    public void Detect_ReturnTypeColon_IsType()
    {
        const string source = "function foo(): ";
        CompletionEngine.Detect(source, source.Length, context: null, ast: null)
            .Should().Be(CompletionContextKind.Type);
    }

    [Fact]
    public void Detect_DoubleColon_IsStaticMember()
    {
        const string source = "User::";
        CompletionEngine.Detect(source, source.Length, context: null, ast: null)
            .Should().Be(CompletionContextKind.StaticMember);
    }

    [Fact]
    public void Detect_Backslash_IsNamespace()
    {
        const string source = "\\App\\";
        CompletionEngine.Detect(source, source.Length, context: null, ast: null)
            .Should().Be(CompletionContextKind.Namespace);
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
        return PositionUtilities.GetPosition(source, index + needle.Length);
    }

    private static Position PositionAfter(string source, string needle) => PositionOf(source, needle);

    private static string[] Labels(CompletionList list)
        => list.Items.Select(item => item.Label).ToArray();

    private static CompletionItem? ItemNamed(CompletionList list, string label)
        => list.Items.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));

    private static CompletionItemKind? KindOf(CompletionList list, string label)
        => ItemNamed(list, label)?.Kind;

    private static string DocumentationText(CompletionItem item)
    {
        if (item.Documentation is { } docs)
        {
            if (docs.TryGetSecond(out MarkupContent markup) && !string.IsNullOrEmpty(markup.Value))
            {
                return markup.Value;
            }

            if (docs.TryGetFirst(out string text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-completion-tests", fileName);
        return new Uri(path);
    }
}
