using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

/// <summary>Hover on a first-class callable value shows the widened parameter types.</summary>
[Trait("Category", "LanguageServer")]
public class CallableWidenedHoverTests
{
    private const string Source = """
        <?tyhp
        namespace Probe;

        class Wallet {
            public int $amount = 0;
            operator convert(int $value): self {
                Wallet $w = new Wallet();
                $w->amount = $value;
                return $w;
            }
        }

        function takeWallet(Wallet $w): int { return $w->amount; }

        function run(): int {
            $fn = takeWallet(...);
            return $fn(5);
        }
        """;

    [Fact]
    public async Task Hover_OnWrappedCallable_ShowsTheWidenedParameter()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "tyhp-hover-tests", "callable-widened.tyhp"));

        await session.Client.NotifyWithParameterObjectAsync(
            Methods.TextDocumentDidOpenName,
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri,
                    LanguageId = "tyhp",
                    Version = 1,
                    Text = Source,
                },
            });
        await session.WaitForAsync(() => session.Server.Workspace.GetDocument(uri)?.LastAnalysisTime is not null);

        var offset = Source.IndexOf("$fn(5)", StringComparison.Ordinal) + 1;
        var hover = await session.Client.InvokeWithParameterObjectAsync<Hover?>(
            Methods.TextDocumentHoverName,
            new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = PositionUtilities.GetPosition(Source, offset),
            });

        hover.Should().NotBeNull();
        var text = hover!.Contents.TryGetThird(out MarkupContent? markup) && markup is not null
            ? markup.Value
            : hover.Contents.Value?.ToString() ?? string.Empty;
        text.Should().Contain("callable(\\Probe\\Wallet|int $w): int $fn");
    }
}
