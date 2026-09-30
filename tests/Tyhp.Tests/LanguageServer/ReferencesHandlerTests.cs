using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class ReferencesHandlerTests
{
    private const string Source = """
        <?tyhp

        class User {
            public string $name;
            public int $age;

            public function greet(): string {
                return "Hi, I'm " . $this->name;
            }
        }

        function createUser(): User {
            $user = new User();
            $user->name = "Alice";
            $user->age = 30;
            return $user;
        }
        """;

    [Fact]
    public async Task References_OnProperty_FindsDeclarationAccessAndAssignment()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("refs-property.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestReferencesAsync(
            session,
            uri,
            PositionOf(Source, "public string $name"),
            includeDeclaration: true);
        locations.Should().HaveCountGreaterThanOrEqualTo(3);
        locations.Should().OnlyContain(item => SameUri(item.Uri, uri));
    }

    [Fact]
    public async Task References_OnFunction_FindsDeclarationAndCallSite()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("refs-function.tyhp");
        const string source = """
            <?tyhp
            function greet(string $name): string { return $name; }
            function run(): void { greet("Ada"); }
            """;
        await OpenAndWaitAsync(session, uri, source);

        Location[] locations = await RequestReferencesAsync(
            session,
            uri,
            PositionOf(source, "function greet"),
            includeDeclaration: true);
        locations.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task References_OnClass_FindsDeclarationTypeAndNew()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("refs-class.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestReferencesAsync(
            session,
            uri,
            PositionOf(Source, "class User"),
            includeDeclaration: true);
        locations.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task References_CrossFile_IncludesOtherFile()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri userUri = FileUri("refs-cross-user.tyhp");
        Uri mainUri = FileUri("refs-cross-main.tyhp");
        const string userSource = "<?tyhp\nnamespace App;\nclass User {}\n";
        const string mainSource = "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n";

        await OpenAndWaitAsync(session, userUri, userSource);
        await OpenAndWaitAsync(session, mainUri, mainSource);

        Location[] locations = await RequestReferencesAsync(
            session,
            userUri,
            PositionOf(userSource, "class User"),
            includeDeclaration: true);
        locations.Should().HaveCountGreaterThanOrEqualTo(3);
        locations.Should().Contain(item => SameUri(item.Uri, userUri));
        locations.Should().Contain(item => SameUri(item.Uri, mainUri));
    }

    [Fact]
    public async Task References_ExcludeDeclaration_OmitsDeclaringOccurrence()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("refs-no-decl.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] withDecl = await RequestReferencesAsync(
            session,
            uri,
            PositionOf(Source, "class User"),
            includeDeclaration: true);
        Location[] withoutDecl = await RequestReferencesAsync(
            session,
            uri,
            PositionOf(Source, "class User"),
            includeDeclaration: false);
        withoutDecl.Length.Should().BeLessThan(withDecl.Length);
        withoutDecl.Should().NotBeEmpty();
    }

    [Fact]
    public async Task References_OnUntypedLocalVariable_FindsAllUsages()
    {
        // Regression test: an untyped local (`$count = 0;`) resolves with a null
        // SymbolLookupResult.Symbol, so its name must come from CurrentSymbolName's
        // PhpVariableAst.VariableToken fallback rather than the (unset) ValueString/
        // Identifier on the AST node.
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("refs-untyped-local.tyhp");
        const string source = """
            <?tyhp
            function run(): int {
                $count = 0;
                $count++;
                return $count;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        Location[] locations = await RequestReferencesAsync(
            session,
            uri,
            PositionOf(source, "$count = 0"),
            includeDeclaration: true);
        locations.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task References_OnWhitespace_ReturnsEmpty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("refs-ws.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestReferencesAsync(
            session,
            uri,
            new Position { Line = 1, Character = 0 },
            includeDeclaration: true);
        locations.Should().BeEmpty();
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

    private static Task<Location[]> RequestReferencesAsync(
        LspTestSession session,
        Uri uri,
        Position position,
        bool includeDeclaration)
    {
        return session.Client.InvokeWithParameterObjectAsync<Location[]>(
            Methods.TextDocumentReferencesName,
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = position,
                Context = new ReferenceContext { IncludeDeclaration = includeDeclaration },
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
        else if (needle.StartsWith("class ", StringComparison.Ordinal)
            || needle.StartsWith("function ", StringComparison.Ordinal)
            || needle.StartsWith("public ", StringComparison.Ordinal))
        {
            int lastSpace = needle.LastIndexOf(' ');
            if (lastSpace >= 0)
            {
                offset = index + lastSpace + 1;
            }
        }

        return PositionUtilities.GetPosition(source, offset);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-references-tests", fileName);
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
