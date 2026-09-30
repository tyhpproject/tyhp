using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class RenameHandlerTests
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
    public async Task PrepareRename_OnProperty_ReturnsIdentifierRange()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-prep.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        ProtocolRange? range = await RequestPrepareRenameAsync(session, uri, PositionOf(Source, "public string $name"));
        range.Should().NotBeNull();
        string text = Slice(Source, range!);
        text.Should().BeOneOf("$name", "name");
    }

    [Fact]
    public async Task PrepareRename_OnBuiltinType_ReturnsNull()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-builtin.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        ProtocolRange? range = await RequestPrepareRenameAsync(session, uri, PositionOf(Source, "string $name"));
        range.Should().BeNull();
    }

    [Fact]
    public async Task PrepareRename_OnTyhpdefSymbol_ReturnsNull()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri defUri = FileUri("user.tyhpdef");
        Uri mainUri = FileUri("rename-tyhpdef-main.tyhp");
        const string defSource = "<?tyhpdef\nnamespace App;\nclass User {}\n";
        const string mainSource = "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n";
        await OpenAndWaitAsync(session, defUri, defSource);
        await OpenAndWaitAsync(session, mainUri, mainSource);

        ProtocolRange? range = await RequestPrepareRenameAsync(session, mainUri, PositionOf(mainSource, ": User"));
        range.Should().BeNull();
    }

    [Fact]
    public async Task Rename_OnProperty_UpdatesDeclarationAndUsages()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-property.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(Source, "public string $name"), "fullName");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        edits.Should().HaveCountGreaterThanOrEqualTo(3);
        string applied = ApplyEdits(Source, edits);
        applied.Should().Contain("$fullName");
        applied.Should().Contain("->fullName");
        applied.Should().NotContain("$name");
        applied.Should().NotContain("->name");
    }

    [Fact]
    public async Task Rename_OnMethod_UpdatesDeclarationAndCallSites()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-method.tyhp");
        const string source = """
            <?tyhp
            class User {
                public function greet(): string { return "hi"; }
            }
            function run(User $user): string { return $user->greet(); }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "function greet"), "display");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        edits.Should().HaveCountGreaterThanOrEqualTo(2);
        string applied = ApplyEdits(source, edits);
        applied.Should().Contain("function display");
        applied.Should().Contain("->display()");
        applied.Should().NotContain("greet");
    }

    [Fact]
    public async Task Rename_RejectsInvalidIdentifier()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-invalid.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(Source, "function greet"), "1bad");
        edit.Should().BeNull();
    }

    [Fact]
    public async Task Rename_RejectsKeyword()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-keyword.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(Source, "function greet"), "class");
        edit.Should().BeNull();
    }

    [Fact]
    public async Task Rename_RejectsBuiltin()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-reject-builtin.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(Source, "string $name"), "str");
        edit.Should().BeNull();
    }

    [Fact]
    public async Task Rename_OnUntypedLocalVariable_UpdatesIncrementAndReassignment()
    {
        // Regression test: an untyped local (`$count = 0;`, no declared type) resolves
        // with a null SymbolLookupResult.Symbol (the binder never binds it into scope —
        // see SymbolFinder.FindLocalVariableDeclaration). CurrentSymbolName must still
        // recover its name from the PhpVariableAst's VariableToken (as HoverHandler's
        // GetHoverName does), or IsRenameable sees an empty name and rejects the rename.
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-untyped-local.tyhp");
        const string source = """
            <?tyhp
            function run(): int {
                $count = 0;
                $count++;
                $count = $count + 1;
                return $count;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "$count = 0"), "total");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        edits.Should().HaveCountGreaterThanOrEqualTo(4);
        string applied = ApplyEdits(source, edits);
        applied.Should().NotContain("$count");
        applied.Should().Contain("$total");
    }

    [Fact]
    public async Task Rename_OnUntypedLocalVariable_IsScopedToEnclosingFunction()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-untyped-local-scope.tyhp");
        const string source = """
            <?tyhp
            function first(): int {
                $value = 1;
                return $value;
            }
            function second(): int {
                $value = 2;
                return $value;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "$value = 1"), "renamed");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        string applied = ApplyEdits(source, edits);
        applied.Should().Contain("$renamed = 1;");
        applied.Should().Contain("$value = 2;", "the same-named local in a different function must not be touched");
    }

    [Fact]
    public async Task Rename_TriggeredAtUntypedLocalMemberAccessUsageSite_RenamesProperty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-untyped-usage-site.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        // Cursor is on the usage site ($user->name) rather than the declaration.
        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(Source, "$user->name ="), "fullName");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        edits.Should().HaveCountGreaterThanOrEqualTo(2);
        string applied = ApplyEdits(Source, edits);
        applied.Should().Contain("->fullName");
        applied.Should().NotContain("$name");
    }

    [Fact]
    public async Task Rename_OnFunctionParameter_UpdatesDeclarationAndUsages()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-parameter.tyhp");
        const string source = """
            <?tyhp
            function greet(string $name): string {
                $greeting = "Hi " . $name;
                return $greeting . $name;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "$name): string"), "personName");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        edits.Should().HaveCountGreaterThanOrEqualTo(3);
        string applied = ApplyEdits(source, edits);
        applied.Should().NotContain("$name");
        applied.Should().Contain("$personName");
    }

    [Fact]
    public async Task Rename_OnStaticMethod_UpdatesDeclarationAndCallSite()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-static-method.tyhp");
        const string source = """
            <?tyhp
            class Factory {
                public static function make(): string { return "x"; }
            }
            function run(): string { return Factory::make(); }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "function make"), "build");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        edits.Should().HaveCountGreaterThanOrEqualTo(2);
        string applied = ApplyEdits(source, edits);
        applied.Should().Contain("function build");
        applied.Should().Contain("Factory::build()");
        applied.Should().NotContain("make");
    }

    [Fact]
    public async Task Rename_RejectsConflictWithExistingLocalVariable()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-local-conflict.tyhp");
        const string source = """
            <?tyhp
            function run(): int {
                $count = 0;
                $total = 10;
                return $count + $total;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "$count = 0"), "total");
        edit.Should().BeNull();
    }

    [Fact]
    public async Task Rename_OnClass_SkipsSelfAndParentKeywordOccurrences()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-class-self-parent.tyhp");
        const string source = """
            <?tyhp
            class Base {
                public static function make(): self {
                    return new self();
                }
            }
            class Derived extends Base {
                public static function build(): parent {
                    return new parent();
                }
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        WorkspaceEdit? edit = await RequestRenameAsync(session, uri, PositionOf(source, "class Base"), "Root");
        edit.Should().NotBeNull();
        TextEdit[] edits = EditsFor(edit!, uri);
        string applied = ApplyEdits(source, edits);
        applied.Should().Contain("class Root {");
        applied.Should().Contain("extends Root");
        applied.Should().Contain("return new self();", "self should not be rewritten to the new class name");
        applied.Should().Contain("return new parent();", "parent should not be rewritten to the new class name");
    }

    [Fact]
    public async Task PrepareRename_OnUntypedLocalVariable_ReturnsRange()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-prep-untyped.tyhp");
        const string source = """
            <?tyhp
            function run(): int {
                $count = 0;
                return $count;
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        ProtocolRange? range = await RequestPrepareRenameAsync(session, uri, PositionOf(source, "$count = 0"));
        range.Should().NotBeNull();
        string text = Slice(source, range!);
        text.Should().BeOneOf("$count", "count");
    }

    [Fact]
    public async Task PrepareRename_OnFunctionParameter_ReturnsRange()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("rename-prep-parameter.tyhp");
        const string source = """
            <?tyhp
            function greet(string $name): string { return $name; }
            """;
        await OpenAndWaitAsync(session, uri, source);

        ProtocolRange? range = await RequestPrepareRenameAsync(session, uri, PositionOf(source, "$name): string"));
        range.Should().NotBeNull();
    }

    [Fact]
    public async Task Rename_CrossFile_EditsBothFiles()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri userUri = FileUri("rename-cross-user.tyhp");
        Uri mainUri = FileUri("rename-cross-main.tyhp");
        const string userSource = "<?tyhp\nnamespace App;\nclass User {}\n";
        const string mainSource = "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n";
        await OpenAndWaitAsync(session, userUri, userSource);
        await OpenAndWaitAsync(session, mainUri, mainSource);

        WorkspaceEdit? edit = await RequestRenameAsync(session, userUri, PositionOf(userSource, "class User"), "Account");
        edit.Should().NotBeNull();
        EditsFor(edit!, userUri).Should().NotBeEmpty();
        EditsFor(edit!, mainUri).Should().NotBeEmpty();
        ApplyEdits(userSource, EditsFor(edit!, userUri)).Should().Contain("class Account");
        ApplyEdits(mainSource, EditsFor(edit!, mainUri)).Should().Contain("Account");
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

    private static Task<ProtocolRange?> RequestPrepareRenameAsync(LspTestSession session, Uri uri, Position position)
    {
        return session.Client.InvokeWithParameterObjectAsync<ProtocolRange?>(
            "textDocument/prepareRename",
            new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = position,
            });
    }

    private static Task<WorkspaceEdit?> RequestRenameAsync(
        LspTestSession session,
        Uri uri,
        Position position,
        string newName)
    {
        return session.Client.InvokeWithParameterObjectAsync<WorkspaceEdit?>(
            Methods.TextDocumentRenameName,
            new RenameParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = position,
                NewName = newName,
            });
    }

    private static TextEdit[] EditsFor(WorkspaceEdit edit, Uri uri)
    {
        string key = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
        if (edit.Changes is not null)
        {
            foreach (KeyValuePair<string, TextEdit[]> pair in edit.Changes)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal)
                    || string.Equals(pair.Key, uri.ToString(), StringComparison.Ordinal)
                    || string.Equals(Uri.UnescapeDataString(pair.Key), key, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }
        }

        return [];
    }

    private static string ApplyEdits(string source, TextEdit[] edits)
    {
        var ordered = edits
            .OrderByDescending(edit => edit.Range.Start.Line)
            .ThenByDescending(edit => edit.Range.Start.Character)
            .ToArray();
        string result = source;
        foreach (TextEdit edit in ordered)
        {
            int start = PositionUtilities.GetOffset(result, edit.Range.Start);
            int end = PositionUtilities.GetOffset(result, edit.Range.End);
            result = result[..start] + edit.NewText + result[end..];
        }

        return result;
    }

    private static string Slice(string source, ProtocolRange range)
    {
        int start = PositionUtilities.GetOffset(source, range.Start);
        int end = PositionUtilities.GetOffset(source, range.End);
        return source[start..end];
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
        string path = Path.Combine(Path.GetTempPath(), "tyhp-rename-tests", fileName);
        return new Uri(path);
    }
}
