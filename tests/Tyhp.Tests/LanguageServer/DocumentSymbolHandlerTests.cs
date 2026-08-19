using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DocumentSymbolHandlerTests
{
    private const string Source = """
        <?tyhp

        namespace App {
            class User {
                public string $name;

                public function greet(): string {
                    return "Hi, I'm " . $this->name;
                }
            }

            function createUser(): User {
                return new User();
            }

            const MAX = 10;

            type UserId = int;

            enum Color {
                case Red;
                case Blue;
            }

            interface Named {
                public function name(): string;
            }
        }
        """;

    [Fact]
    public async Task DocumentSymbol_ReturnsHierarchicalOutline()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("outline.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        DocumentSymbol[] symbols = await RequestSymbolsAsync(session, uri);
        DocumentSymbol ns = Find(symbols, "App");
        ns.Kind.Should().Be(SymbolKind.Namespace);

        DocumentSymbol user = Find(ns.Children ?? [], "User");
        user.Kind.Should().Be(SymbolKind.Class);
        Find(user.Children ?? [], "$name").Kind.Should().Be(SymbolKind.Property);
        Find(user.Children ?? [], "greet").Kind.Should().Be(SymbolKind.Method);

        Find(ns.Children ?? [], "createUser").Kind.Should().Be(SymbolKind.Function);
        Find(ns.Children ?? [], "MAX").Kind.Should().Be(SymbolKind.Constant);
        Find(ns.Children ?? [], "UserId").Kind.Should().Be(SymbolKind.TypeParameter);

        DocumentSymbol color = Find(ns.Children ?? [], "Color");
        color.Kind.Should().Be(SymbolKind.Enum);
        Find(color.Children ?? [], "Red").Kind.Should().Be(SymbolKind.EnumMember);

        Find(ns.Children ?? [], "Named").Kind.Should().Be(SymbolKind.Interface);
    }

    [Fact]
    public async Task DocumentSymbol_SelectionRangeIsInsideRange()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("outline-range.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        DocumentSymbol[] symbols = await RequestSymbolsAsync(session, uri);
        DocumentSymbol user = Find(Find(symbols, "App").Children ?? [], "User");
        user.Range.Should().NotBeNull();
        user.SelectionRange.Should().NotBeNull();
        user.SelectionRange.Start.Line.Should().BeGreaterThanOrEqualTo(user.Range.Start.Line);
        user.SelectionRange.End.Line.Should().BeLessThanOrEqualTo(user.Range.End.Line);
        user.SelectionRange.Start.Line.Should().Be(PositionOf(Source, "class User").Line);
    }

    [Fact]
    public async Task DocumentSymbol_EmptyFile_ReturnsEmpty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("outline-empty.tyhp");
        await OpenAndWaitAsync(session, uri, "<?tyhp\n");

        DocumentSymbol[] symbols = await RequestSymbolsAsync(session, uri);
        symbols.Should().BeEmpty();
    }

    [Fact]
    public async Task DocumentSymbol_TraitAndStructKinds()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("outline-kinds.tyhp");
        const string source = """
            <?tyhp
            trait Loggable {
                public function log(): void {}
            }
            struct Point {
                int $x;
                int $y;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        DocumentSymbol[] symbols = await RequestSymbolsAsync(session, uri);
        Find(symbols, "Loggable").Kind.Should().Be(SymbolKind.Class);
        DocumentSymbol point = Find(symbols, "Point");
        point.Kind.Should().Be(SymbolKind.Struct);
        Find(point.Children ?? [], "$x").Kind.Should().Be(SymbolKind.Property);
    }

    [Fact]
    public async Task DocumentSymbol_BraceLessNamespace_NestsFollowingDeclarations()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("outline-braceless.tyhp");
        const string source = """
            <?tyhp
            namespace App\Models;

            class User {
                public string $name;
            }

            function createUser(): User {
                return new User();
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        DocumentSymbol[] symbols = await RequestSymbolsAsync(session, uri);
        DocumentSymbol ns = Find(symbols, "App\\Models");
        ns.Kind.Should().Be(SymbolKind.Namespace);
        Find(ns.Children ?? [], "User").Kind.Should().Be(SymbolKind.Class);
        Find(ns.Children ?? [], "createUser").Kind.Should().Be(SymbolKind.Function);
    }

    [Fact]
    public async Task DocumentSymbol_ConstructorMethod_HasConstructorKind()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("outline-constructor.tyhp");
        const string source = """
            <?tyhp
            class Adder {
                public function __construct(int $seed) {}
                public static function make(int $seed): Adder {
                    return new Adder($seed);
                }
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        DocumentSymbol[] symbols = await RequestSymbolsAsync(session, uri);
        DocumentSymbol adder = Find(symbols, "Adder");
        Find(adder.Children ?? [], "__construct").Kind.Should().Be(SymbolKind.Constructor);
        Find(adder.Children ?? [], "make").Kind.Should().Be(SymbolKind.Method);
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

    private static Task<DocumentSymbol[]> RequestSymbolsAsync(LspTestSession session, Uri uri)
    {
        return session.Client.InvokeWithParameterObjectAsync<DocumentSymbol[]>(
            Methods.TextDocumentDocumentSymbolName,
            new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });
    }

    private static DocumentSymbol Find(IEnumerable<DocumentSymbol> symbols, string name)
    {
        DocumentSymbol? match = symbols.FirstOrDefault(symbol => string.Equals(symbol.Name, name, StringComparison.Ordinal));
        match.Should().NotBeNull($"expected symbol '{name}' among {string.Join(", ", symbols.Select(s => s.Name))}");
        return match!;
    }

    private static Position PositionOf(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0);
        int offset = index;
        if (needle.StartsWith("class ", StringComparison.Ordinal))
        {
            offset = index + needle.LastIndexOf(' ') + 1;
        }

        return PositionUtilities.GetPosition(source, offset);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-docsymbol-tests", fileName);
        return new Uri(path);
    }
}
