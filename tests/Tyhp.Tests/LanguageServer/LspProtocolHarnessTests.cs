using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Enums;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

/// <summary>
/// Story 19 golden-acceptance harness: one JSON-RPC session covering
/// initialize capabilities, didOpen → diagnostics, completion / definition / hover,
/// and shutdown/exit. Uses the in-memory Content-Length transport equivalent of stdio.
/// </summary>
[Trait("Category", "LanguageServer")]
[Trait("Category", "Integration")]
public class LspProtocolHarnessTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private const string InvalidSource = """
        <?tyhp

        function greet(string $name): string {
            return "Hello, " . $name
        }
        """;

    private const string ValidSource = """
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

        function testCompletion(): void {
            $user = new User();
            $user->greet();
        }
        """;

    [Fact]
    public async Task ProtocolSession_InitializeDidOpenFeaturesShutdownExit()
    {
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            await using var session = await LspTestSession.StartAsync();

            InitializeResult init = await session.InitializeAsync();
            init.Capabilities.Should().NotBeNull();
            init.Capabilities.TextDocumentSync.Should().NotBeNull();
            AssertIncrementalSync(init.Capabilities.TextDocumentSync!);
            init.Capabilities.CompletionProvider.Should().NotBeNull();
            init.Capabilities.DefinitionProvider.Should().NotBeNull();
            init.Capabilities.HoverProvider.Should().NotBeNull();

            Uri invalidUri = FileUri("invalid.tyhp");
            await OpenAsync(session, invalidUri, InvalidSource, version: 1);
            await session.WaitForDiagnosticsAsync(
                invalidUri,
                d => d.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error));
            session.Notifications.LastFor(invalidUri)!.Diagnostics
                .Should()
                .Contain(item => item.Severity == DiagnosticSeverity.Error);

            Uri uri = FileUri("harness.tyhp");
            await OpenAsync(session, uri, ValidSource, version: 1);
            await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null);

            CompletionList completion = await session.Client.InvokeWithParameterObjectAsync<CompletionList>(
                Methods.TextDocumentCompletionName,
                new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = uri },
                    Position = PositionAtEnd(ValidSource, "$user->"),
                });
            string[] labels = completion.Items.Select(item => item.Label).ToArray();
            labels.Should().Contain("name");
            labels.Should().Contain("age");
            labels.Should().Contain("greet");

            Location[] definition = await session.Client.InvokeWithParameterObjectAsync<Location[]>(
                Methods.TextDocumentDefinitionName,
                new TextDocumentPositionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = uri },
                    Position = PositionOnToken(ValidSource, ": User"),
                });
            definition.Should().NotBeEmpty();
            definition[0].Range.Start.Line.Should().Be(PositionOnToken(ValidSource, "class User").Line);

            Hover? hover = await session.Client.InvokeWithParameterObjectAsync<Hover?>(
                Methods.TextDocumentHoverName,
                new TextDocumentPositionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = uri },
                    Position = PositionOnToken(ValidSource, "return $user"),
                });
            hover.Should().NotBeNull();
            HoverText(hover!).Should().Contain("User");

            object? shutdown = await session.Client.InvokeAsync<object?>(Methods.ShutdownName);
            shutdown.Should().BeNull();
            await session.Client.NotifyAsync(Methods.ExitName);
            await session.ServerTask.WaitAsync(TestTimeout);
            Environment.ExitCode.Should().Be((int)ExitCode.Success);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    private static void AssertIncrementalSync(SumType<TextDocumentSyncKind, TextDocumentSyncOptions> sync)
    {
        if (sync.Value is TextDocumentSyncOptions options)
        {
            options.Change.Should().Be(TextDocumentSyncKind.Incremental);
            return;
        }

        if (sync.Value is TextDocumentSyncKind kind)
        {
            kind.Should().Be(TextDocumentSyncKind.Incremental);
            return;
        }

        true.Should().BeFalse("textDocumentSync should be Incremental options or kind");
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

    private static Position PositionAtEnd(string source, string needle)
    {
        int index = source.LastIndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist in source");
        return PositionUtilities.GetPosition(source, index + needle.Length);
    }

    private static Position PositionOnToken(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist in source");
        int offset = index;
        if (needle.StartsWith(": ", StringComparison.Ordinal)
            || needle.StartsWith("class ", StringComparison.Ordinal)
            || needle.StartsWith("return ", StringComparison.Ordinal))
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
        string path = Path.Combine(Path.GetTempPath(), "tyhp-lsp-harness-tests", fileName);
        return new Uri(path);
    }
}
