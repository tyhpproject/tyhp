using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Configuration;
using Tyhp.LanguageServer.Workspace;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DocumentAnalysisTests
{
    private const string ValidSource = "<?tyhp\nfunction greet(): void {}\n";
    private const string InvalidSource = """
        <?tyhp

        function greet(string $name): string {
            return "Hello, " . $name
        }
        """;

    [Fact]
    public void ParseFromContent_ValidSource_ReturnsAstWithoutErrors()
    {
        using var compilation = new CompilationService();
        var diagnostics = new Tyhp.Domain.Diagnostics.DiagnosticBag();
        var ast = compilation.ParseFromContent(ValidSource, "ok.tyhp", diagnostics);
        ast.Should().NotBeNull();
        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ParseFromContent_InvalidSyntax_ReportsDiagnostics()
    {
        using var compilation = new CompilationService();
        var diagnostics = new Tyhp.Domain.Diagnostics.DiagnosticBag();
        compilation.ParseFromContent(InvalidSource, "bad.tyhp", diagnostics);
        diagnostics.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task DidOpen_ValidFile_PublishesDiagnosticsAndStoresAst()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("opened-valid.tyhp");

        await OpenAsync(session, uri, ValidSource, version: 1);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null);
        await session.WaitForDiagnosticsAsync(uri);

        DocumentState state = session.Server.Workspace.GetDocument(uri)!;
        state.ParsedAst.Should().NotBeNull();
        state.IsDirty.Should().BeFalse();
        PublishDiagnosticParams? published = session.Notifications.LastFor(uri);
        published.Should().NotBeNull();
        published!.Diagnostics.Should().NotContain(d => d.Severity == Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DidOpen_InvalidSyntax_PublishesErrorDiagnostics()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("opened-invalid.tyhp");

        await OpenAsync(session, uri, InvalidSource, version: 1);
        await session.WaitForDiagnosticsAsync(uri, d => d.Diagnostics.Length > 0);

        session.Notifications.LastFor(uri)!.Diagnostics.Should().Contain(d => d.Severity == Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DidChange_FixingSyntax_ClearsErrorDiagnostics()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("fix-syntax.tyhp");

        await OpenAsync(session, uri, InvalidSource, version: 1);
        await session.WaitForDiagnosticsAsync(uri, d => d.Diagnostics.Any(item => item.Severity == Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error));

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidChangeName,
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
                ContentChanges = [new TextDocumentContentChangeEvent { Text = ValidSource }],
            });

        await session.WaitForDiagnosticsAsync(
            uri,
            d => d.Diagnostics.All(item => item.Severity != Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task DidClose_PublishesEmptyDiagnostics()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("close-diagnostics.tyhp");

        await OpenAsync(session, uri, InvalidSource, version: 1);
        await session.WaitForDiagnosticsAsync(uri, d => d.Diagnostics.Length > 0);

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidCloseName,
            new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
            });

        await session.WaitForDiagnosticsAsync(uri, d => d.Diagnostics.Length == 0);
        session.Server.Workspace.GetDocument(uri).Should().BeNull();
    }

    [Fact]
    public async Task SameFileNameDifferentFolders_DiagnosticsDoNotCollide()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();

        Uri fooUri = new Uri(Path.Combine(Path.GetTempPath(), "tyhp-analysis-tests", "foo", "Utils.tyhp"));
        Uri barUri = new Uri(Path.Combine(Path.GetTempPath(), "tyhp-analysis-tests", "bar", "Utils.tyhp"));

        await OpenAsync(session, fooUri, InvalidSource, version: 1);
        await OpenAsync(session, barUri, ValidSource, version: 1);

        await session.WaitForDiagnosticsAsync(fooUri, d => d.Diagnostics.Length > 0);
        await session.WaitForDiagnosticsAsync(barUri);

        // Both files are named "Utils.tyhp" but live in different folders. The parse error that
        // belongs to foo/Utils.tyhp must not also be attributed to bar/Utils.tyhp via a bare
        // filename fallback.
        session.Notifications.LastFor(barUri)!.Diagnostics
            .Should().NotContain(d => d.Severity == Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DidClose_UnsavedBuffer_NoLongerInfluencesOtherOpenDocuments()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();

        Uri barUri = FileUri("close-refresh-bar.tyhp");
        Uri mainUri = FileUri("close-refresh-main.tyhp");
        const string BarSource = "<?tyhp\nnamespace App;\nclass Bar {}\n";
        const string MainSource = "<?tyhp\nnamespace App;\nfunction useBar(Bar $bar): void {}\n";

        // Bar.tyhp is opened but never written to disk (an unsaved, never-saved buffer).
        await OpenAsync(session, barUri, BarSource, version: 1);
        await OpenAsync(session, mainUri, MainSource, version: 1);
        await session.WaitForDiagnosticsAsync(
            mainUri,
            d => d.Diagnostics.All(item => item.Severity != Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error));

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidCloseName,
            new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier { Uri = barUri } });

        // Bar.tyhp never existed on disk, so closing it must drop its class from the project-wide
        // AST cache immediately rather than letting the discarded buffer keep resolving `Bar` for
        // other open documents.
        await session.WaitForDiagnosticsAsync(
            mainUri,
            d => d.Diagnostics.Any(item => item.Severity == Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task DidOpen_PhpFile_DoesNotAnalyze()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = new Uri(Path.Combine(Path.GetTempPath(), "tyhp-analysis-tests", "skip.php"));

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri,
                    LanguageId = "php",
                    Version = 1,
                    Text = "<?php echo 1;",
                },
            });

        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri) is not null);
        await Task.Delay(150);

        session.Server.Workspace.GetDocument(uri)!.ParsedAst.Should().BeNull();
        session.Server.Workspace.GetDocument(uri)!.LastAnalysisTime.Should().BeNull();
        session.Notifications.LastFor(uri).Should().BeNull();
    }

    [Fact]
    public async Task RapidDidChange_DebouncesToSingleSettledAnalysis()
    {
        var config = new ServerConfiguration
        {
            DebounceDelay = 80,
            CompilationOptions = new CompilationOptions
            {
                EnableAstCache = false,
                ProjectPath = Path.GetTempPath(),
            },
        };
        await using var session = await LspTestSession.StartAsync(configuration: config);
        await session.InitializeAsync();
        Uri uri = FileUri("debounce.tyhp");

        await OpenAsync(session, uri, ValidSource, version: 1);
        await session.WaitForDiagnosticsAsync(uri);
        int afterOpen = session.Notifications.Diagnostics.Count(d => SameUri(d.Uri, uri));

        for (int i = 0; i < 8; i++)
        {
            await session.Client.NotifyWithParameterObjectAsync(
                Methods.TextDocumentDidChangeName,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = i + 2 },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Text = $"<?tyhp\nfunction f{i}(): void {{}}\n",
                        },
                    ],
                });
        }

        await session.WaitForAsync(() =>
            session.Server.Workspace.GetDocument(uri)?.Content.Contains("function f7", StringComparison.Ordinal) == true
            && session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null
            && session.Server.Workspace.GetDocument(uri)?.IsDirty == false);

        int afterTyping = session.Notifications.Diagnostics.Count(d => SameUri(d.Uri, uri));
        (afterTyping - afterOpen).Should().BeLessThan(8);
        (afterTyping - afterOpen).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task EnableDiagnosticsFalse_DoesNotPublish()
    {
        var config = new ServerConfiguration
        {
            EnableDiagnostics = false,
            CompilationOptions = new CompilationOptions
            {
                EnableAstCache = false,
                ProjectPath = Path.GetTempPath(),
            },
        };
        await using var session = await LspTestSession.StartAsync(configuration: config);
        await session.InitializeAsync();
        Uri uri = FileUri("no-publish.tyhp");

        await OpenAsync(session, uri, InvalidSource, version: 1);
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null);
        await Task.Delay(50);

        session.Notifications.LastFor(uri).Should().BeNull();
    }

    [Fact]
    public async Task WorkspaceScan_IncludesProjectFilesInGlobalScope()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithTyhpFile("User.tyhp", """
                <?tyhp
                namespace App;
                class User {}
                """)
            .WithTyhpFile("Main.tyhp", """
                <?tyhp
                namespace App;
                function main(): void {}
                """);
        var built = project.BuildProject();
        var config = ServerConfiguration.FromProject(built);
        config.CompilationOptions.EnableAstCache = false;

        await using var session = await LspTestSession.StartAsync(configuration: config);
        Uri rootUri = new Uri(project.ProjectDirectory + Path.DirectorySeparatorChar);
        await session.InitializeAsync(rootUri);
        await session.NotifyInitializedAsync();

        Uri mainUri = new Uri(Path.Combine(project.ProjectDirectory, "Main.tyhp"));
        await OpenAsync(
            session,
            mainUri,
            File.ReadAllText(Path.Combine(project.ProjectDirectory, "Main.tyhp")),
            version: 1);
        await session.WaitForAsync(() => session.Server.Analysis.GetGlobalScope() is not null);

        session.Server.Analysis.GetGlobalScope().Should().NotBeNull();
        session.Server.Workspace.GetDocument(mainUri)!.ParsedAst.Should().NotBeNull();
    }

    private static Task OpenAsync(LspTestSession session, Uri uri, string text, int version)
    {
        return session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri,
                    LanguageId = "tyhp",
                    Version = version,
                    Text = text,
                },
            });
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-analysis-tests", fileName);
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
