using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Exceptions;
using Tyhp.LanguageServer;
using Tyhp.LanguageServer.Analysis;
using Tyhp.LanguageServer.Configuration;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class CodeActionHandlerTests
{
    [Fact]
    public async Task CodeAction_UnresolvedReturnType_OffersAutoImport()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri modelsUri = FileUri("Models/User.tyhp");
        Uri mainUri = FileUri("Controllers/main.tyhp");
        const string models = "<?tyhp\nnamespace App\\Models;\nclass User {}\n";
        const string main = """
            <?tyhp
            namespace App\Controllers;
            function getUser(): User {
                return new User();
            }
            """;
        await OpenAndWaitAsync(session, modelsUri, models);
        await OpenAndWaitAsync(session, mainUri, main);
        await session.WaitForDiagnosticsAsync(
            mainUri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.BinderUnresolvedReturnType)));

        CodeAction[] actions = await RequestCodeActionsAsync(
            session,
            mainUri,
            RangeOf(main, "User"));
        CodeAction? import = actions.FirstOrDefault(a =>
            a.Title.Contains("App\\Models\\User", StringComparison.Ordinal));
        import.Should().NotBeNull();
        import!.Kind.Should().Be(CodeActionKind.QuickFix);
        import.Edit.Should().NotBeNull();
        import.Edit!.Changes.Should().NotBeNull();
        import.Edit.Changes!.Values.SelectMany(edits => edits).Should().Contain(edit =>
            edit.NewText.Contains("use App\\Models\\User;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_UnresolvedNullableReturnType_OffersAutoImport()
    {
        // The binder's type-display formatter prefixes an unresolved nullable return type with
        // "?" (e.g. "?User") in the diagnostic's FormatParams. Auto-import must strip that marker
        // before matching a symbol name, or it silently offers nothing.
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri modelsUri = FileUri("Models/NullableUser.tyhp");
        Uri mainUri = FileUri("Controllers/nullable-main.tyhp");
        const string models = "<?tyhp\nnamespace App\\Models;\nclass User {}\n";
        const string main = """
            <?tyhp
            namespace App\Controllers;
            function findUser(): ?User {
                return null;
            }
            """;
        await OpenAndWaitAsync(session, modelsUri, models);
        await OpenAndWaitAsync(session, mainUri, main);
        await session.WaitForDiagnosticsAsync(
            mainUri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.BinderUnresolvedReturnType)));

        CodeAction[] actions = await RequestCodeActionsAsync(
            session,
            mainUri,
            RangeOf(main, "User"));
        CodeAction? import = actions.FirstOrDefault(a =>
            a.Title.Contains("App\\Models\\User", StringComparison.Ordinal));
        import.Should().NotBeNull();
        import!.Edit!.Changes!.Values.SelectMany(edits => edits).Should().Contain(edit =>
            edit.NewText.Contains("use App\\Models\\User;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeAction_UnusedImport_OffersRemove()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("unused-import.tyhp");
        const string source = """
            <?tyhp
            namespace App;
            use App\Missing\Thing;
            class Demo {}
            """;
        await OpenAndWaitAsync(session, uri, source);
        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.Any(item => IsCode(item, MessageCode.CheckerUnusedImport)));

        CodeAction[] actions = await RequestCodeActionsAsync(
            session,
            uri,
            RangeOf(source, "use App\\Missing\\Thing"));
        CodeAction? remove = actions.FirstOrDefault(a =>
            a.Kind == CodeActionKind.QuickFix
            && a.Title.Contains("Thing", StringComparison.Ordinal));
        remove.Should().NotBeNull();
        remove!.Edit.Should().NotBeNull();
        string replacement = ApplyFirstEdit(source, uri, remove.Edit!);
        replacement.Should().NotContain("use App\\Missing\\Thing;");
        replacement.Should().Contain("class Demo");
    }

    [Fact]
    public async Task CodeAction_UnsortedImports_OffersOrganizeImports()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("organize.tyhp");
        const string source = """
            <?tyhp
            use Zzz\A;
            use Aaa\B;
            class Demo {}
            """;
        await OpenAndWaitAsync(session, uri, source);

        CodeAction[] actions = await RequestCodeActionsAsync(
            session,
            uri,
            RangeOf(source, "class Demo"));
        CodeAction? organize = actions.FirstOrDefault(a => a.Kind == CodeActionKind.SourceOrganizeImports);
        organize.Should().NotBeNull();
        string replacement = ApplyFirstEdit(source, uri, organize!.Edit!);
        int aaa = replacement.IndexOf("use Aaa\\B;", StringComparison.Ordinal);
        int zzz = replacement.IndexOf("use Zzz\\A;", StringComparison.Ordinal);
        aaa.Should().BeGreaterThanOrEqualTo(0);
        zzz.Should().BeGreaterThan(aaa);
    }

    [Fact]
    public async Task CodeAction_UnknownDocument_ReturnsEmpty()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        CodeAction[] actions = await RequestCodeActionsAsync(
            session,
            FileUri("missing.tyhp"),
            new ProtocolRange
            {
                Start = new Position { Line = 0, Character = 0 },
                End = new Position { Line = 0, Character = 1 },
            });
        actions.Should().BeEmpty();
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
        string path = Path.Combine(Path.GetTempPath(), "tyhp-codeaction-tests", fileName);
        return new Uri(path);
    }
}
