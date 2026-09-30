using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class WorkspaceSymbolHandlerTests
{
    private const string Models = """
        <?tyhp

        namespace App\Models;

        class User {
            public string $name;
        }

        enum Color {
            case Red;
        }

        type UserId = int;

        const MAX = 10;
        """;

    private const string Services = """
        <?tyhp

        namespace App\Services;

        function createUser(): \App\Models\User {
            return new \App\Models\User();
        }
        """;

    [Fact]
    public async Task WorkspaceSymbol_FindsClassByName()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("User.tyhp"), Models);
        await OpenAndWaitAsync(session, FileUri("Factory.tyhp"), Services);

        SymbolInformation[] symbols = await RequestAsync(session, "User");
        symbols.Should().Contain(symbol =>
            symbol.Name == "User" && symbol.Kind == SymbolKind.Class);
        symbols.Should().NotContain(symbol => symbol.Name == "$name" || symbol.Name == "name");
    }

    [Fact]
    public async Task WorkspaceSymbol_FindsFunctionAcrossFiles()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("User.tyhp"), Models);
        await OpenAndWaitAsync(session, FileUri("Factory.tyhp"), Services);

        SymbolInformation[] symbols = await RequestAsync(session, "createUser");
        SymbolInformation match = symbols.Should().ContainSingle(symbol => symbol.Name == "createUser").Subject;
        match.Kind.Should().Be(SymbolKind.Function);
        match.Location.Should().NotBeNull();
        match.Location.Uri.Should().NotBeNull();
        match.Location.Uri!.AbsoluteUri.Should().Contain("Factory.tyhp");
    }

    [Fact]
    public async Task WorkspaceSymbol_FuzzyMatchFindsClass()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("User.tyhp"), Models);

        SymbolInformation[] symbols = await RequestAsync(session, "Usr");
        symbols.Should().Contain(symbol => symbol.Name == "User");
    }

    [Fact]
    public async Task WorkspaceSymbol_QualifiedNameSearch()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("User.tyhp"), Models);

        SymbolInformation[] symbols = await RequestAsync(session, "Models\\User");
        symbols.Should().Contain(symbol => symbol.Name == "User" && symbol.Kind == SymbolKind.Class);
    }

    [Fact]
    public async Task WorkspaceSymbol_IncludesConstantsTypeAliasesAndEnumCases()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("User.tyhp"), Models);

        (await RequestAsync(session, "MAX")).Should().Contain(symbol =>
            symbol.Name == "MAX" && symbol.Kind == SymbolKind.Constant);
        (await RequestAsync(session, "UserId")).Should().Contain(symbol =>
            symbol.Name == "UserId" && symbol.Kind == SymbolKind.TypeParameter);
        (await RequestAsync(session, "Red")).Should().Contain(symbol =>
            symbol.Name == "Red" && symbol.Kind == SymbolKind.EnumMember);
        (await RequestAsync(session, "Color")).Should().Contain(symbol =>
            symbol.Name == "Color" && symbol.Kind == SymbolKind.Enum);
    }

    [Fact]
    public async Task WorkspaceSymbol_FindsInterfaceAndTrait()
    {
        const string InterfaceAndTraitSource = """
            <?tyhp

            interface Greetable {
                public function greet(): string;
            }

            trait Loud {
                public function shout(): string {
                    return "LOUD";
                }
            }
            """;
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("Greetable.tyhp"), InterfaceAndTraitSource);

        SymbolInformation[] symbols = await RequestAsync(session, "");
        symbols.Should().Contain(symbol => symbol.Name == "Greetable" && symbol.Kind == SymbolKind.Interface);
        symbols.Should().Contain(symbol => symbol.Name == "Loud" && symbol.Kind == SymbolKind.Class);
    }

    [Fact]
    public async Task WorkspaceSymbol_EmptyQuery_CapsResults()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        await OpenAndWaitAsync(session, FileUri("many.tyhp"), ManyFunctionsSource(120));

        SymbolInformation[] symbols = await RequestAsync(session, "");
        symbols.Length.Should().BeLessThanOrEqualTo(WorkspaceSymbolSearch.MaxResults);
        symbols.Should().NotBeEmpty();
    }

    [Fact]
    public void Score_PrefersExactThenPrefixThenSubstringThenFuzzy()
    {
        var exact = new TypeAliasSymbol("User");
        var prefix = new TypeAliasSymbol("UserService");
        var substring = new TypeAliasSymbol("GetUser");
        var fuzzy = new TypeAliasSymbol("UserRecord");

        WorkspaceSymbolSearch.Score("User", exact, qualified: false)
            .Should().BeGreaterThan(WorkspaceSymbolSearch.Score("User", prefix, qualified: false));
        WorkspaceSymbolSearch.Score("User", prefix, qualified: false)
            .Should().BeGreaterThan(WorkspaceSymbolSearch.Score("User", substring, qualified: false));
        WorkspaceSymbolSearch.Score("Usr", fuzzy, qualified: false).Should().BeGreaterThan(0);
        WorkspaceSymbolSearch.Score("zzz", exact, qualified: false).Should().Be(0);
    }

    private static string ManyFunctionsSource(int count)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("<?tyhp");
        builder.AppendLine();
        for (int i = 0; i < count; i++)
        {
            builder.Append("function fn");
            builder.Append(i);
            builder.AppendLine("(): void {}");
        }

        return builder.ToString();
    }

    private static Task<SymbolInformation[]> RequestAsync(LspTestSession session, string query)
    {
        return session.Client.InvokeWithParameterObjectAsync<SymbolInformation[]>(
            Methods.WorkspaceSymbolName,
            new WorkspaceSymbolParams { Query = query });
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

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-workspace-symbol-tests", fileName);
        return new Uri(path);
    }
}
