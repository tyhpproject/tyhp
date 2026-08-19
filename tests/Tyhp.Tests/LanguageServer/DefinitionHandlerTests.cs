using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DefinitionHandlerTests
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
    public async Task Definition_OnClassNameInReturnType_JumpsToClassDeclaration()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("definitions.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(Source, ": User"));
        locations.Should().NotBeEmpty();
        SameUri(locations[0].Uri, uri).Should().BeTrue();
        locations[0].Range.Start.Line.Should().Be(PositionOf(Source, "class User").Line);
    }

    [Fact]
    public async Task Definition_OnPropertyAccess_JumpsToPropertyDeclaration()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("def-property.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(Source, "$this->name"));
        locations.Should().NotBeEmpty();
        locations[0].Range.Start.Line.Should().Be(PositionOf(Source, "public string $name").Line);
    }

    [Fact]
    public async Task Definition_OnFunctionName_JumpsToFunctionDeclaration()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("def-function.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(Source, "function createUser"));
        locations.Should().NotBeEmpty();
        locations[0].Range.Start.Line.Should().Be(PositionOf(Source, "function createUser").Line);
    }

    [Fact]
    public async Task Definition_OnNewClassName_JumpsToClassDeclaration()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("def-new.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(Source, "new User()"));
        locations.Should().NotBeEmpty();
        locations[0].Range.Start.Line.Should().Be(PositionOf(Source, "class User").Line);
    }

    [Fact]
    public async Task Definition_CrossFile_JumpsToOtherTyhpFile()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri userUri = FileUri("cross-user.tyhp");
        Uri mainUri = FileUri("cross-main.tyhp");
        const string userSource = "<?tyhp\nnamespace App;\nclass User {}\n";
        const string mainSource = "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n";

        await OpenAndWaitAsync(session, userUri, userSource);
        await OpenAndWaitAsync(session, mainUri, mainSource);

        Location[] locations = await RequestDefinitionAsync(session, mainUri, PositionOf(mainSource, ": User"));
        locations.Should().NotBeEmpty();
        SameUri(locations[0].Uri, userUri).Should().BeTrue();
        locations[0].Range.Start.Line.Should().Be(PositionOf(userSource, "class User").Line);
    }

    [Fact]
    public async Task Definition_OnTyhpdefClass_JumpsToTyhpdefFile()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri defUri = FileUri("user.tyhpdef");
        Uri mainUri = FileUri("tyhpdef-main.tyhp");
        const string defSource = "<?tyhpdef\nnamespace App;\nclass User {}\n";
        const string mainSource = "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n";

        await OpenAndWaitAsync(session, defUri, defSource);
        await OpenAndWaitAsync(session, mainUri, mainSource);

        Location[] locations = await RequestDefinitionAsync(session, mainUri, PositionOf(mainSource, ": User"));
        locations.Should().NotBeEmpty();
        SameUri(locations[0].Uri, defUri).Should().BeTrue();
        locations[0].Range.Start.Line.Should().Be(PositionOf(defSource, "class User").Line);
    }

    [Fact]
    public async Task Definition_OnSelf_JumpsToContainingClass()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("def-self.tyhp");
        const string source = """
            <?tyhp
            class User {
                public static function create(): self {
                    return new self();
                }
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(source, ": self"));
        locations.Should().NotBeEmpty();
        SameUri(locations[0].Uri, uri).Should().BeTrue();
        locations[0].Range.Start.Line.Should().Be(PositionOf(source, "class User").Line);
    }

    [Fact]
    public async Task Definition_OnWhitespace_ReturnsEmpty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("def-whitespace.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        Location[] locations = await RequestDefinitionAsync(session, uri, new Position { Line = 1, Character = 0 });
        locations.Should().BeEmpty();
    }

    [Fact]
    public async Task Definition_OnMagicConstant_ReturnsEmpty()
    {
        // Magic constants (and other compiler builtins with no declaring AST node or source
        // file) have nowhere to navigate. Without filtering, ToSourceUri/ToDefinitionRange fall
        // back to a bogus (0,0) location in the current file instead of an empty result.
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("def-magic-constant.tyhp");
        const string source = "<?tyhp\nfunction where(): string { return __LINE__; }\n";
        await OpenAndWaitAsync(session, uri, source);

        Location[] locations = await RequestDefinitionAsync(session, uri, PositionOf(source, "__LINE__"));
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
        else if (needle.StartsWith(": ", StringComparison.Ordinal)
            || needle.StartsWith("new ", StringComparison.Ordinal)
            || needle.StartsWith("class ", StringComparison.Ordinal)
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
        string path = Path.Combine(Path.GetTempPath(), "tyhp-definition-tests", fileName);
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
