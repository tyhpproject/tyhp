using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.LanguageServer.Analysis;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class SignatureHelpHandlerTests
{
    private const string Source = """
        <?tyhp

        function calculate(int $a, float $b, string $label): string {
            return $label . ": " . ($a + $b);
        }

        class Adder {
            public function __construct(int $seed) {}
            public function add(int $x, int $y): int {
                return $x + $y;
            }
            public static function make(int $seed): Adder {
                return new Adder($seed);
            }
        }
        """;

    [Fact]
    public async Task SignatureHelp_AfterOpenParen_HighlightsFirstParameter()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-first.tyhp");
        string source = Source + "\n$result = calculate(\n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "calculate("));
        help.Should().NotBeNull();
        help!.Signatures.Should().NotBeEmpty();
        help.Signatures[0].Label.Should().Contain("calculate");
        help.Signatures[0].Label.Should().Contain("$a");
        help.ActiveParameter.Should().Be(0);
        help.Signatures[0].Parameters.Should().HaveCount(3);
    }

    [Fact]
    public async Task SignatureHelp_AfterComma_HighlightsNextParameter()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-comma.tyhp");
        string source = Source + "\n$result = calculate(1, \n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "calculate(1, "));
        help.Should().NotBeNull();
        help!.ActiveParameter.Should().Be(1);
        help.Signatures[0].Parameters.Should().HaveCount(3);
        string second = ParameterLabel(help.Signatures[0].Parameters![1]);
        second.Should().Contain("$b");
    }

    [Fact]
    public async Task SignatureHelp_ThirdParameter()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-third.tyhp");
        string source = Source + "\n$result = calculate(1, 2.5, \n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "calculate(1, 2.5, "));
        help.Should().NotBeNull();
        help!.ActiveParameter.Should().Be(2);
    }

    [Fact]
    public async Task SignatureHelp_MethodCall()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-method.tyhp");
        string source = Source + "\nfunction run(Adder $adder): int {\n    return $adder->add();\n}\n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "$adder->add("));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("add");
        help.Signatures[0].Parameters.Should().HaveCount(2);
        help.ActiveParameter.Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_StaticMethodCall()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-static.tyhp");
        string source = Source + "\n$a = Adder::make();\n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "Adder::make("));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("make");
        help.ActiveParameter.Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_ConstructorCall()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-ctor.tyhp");
        string source = Source + "\n$a = new Adder();\n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "new Adder("));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("__construct");
        help.Signatures[0].Parameters.Should().HaveCount(1);
        help.ActiveParameter.Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_NestedCall_ResolvesOuterFunction()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-nested.tyhp");
        string source = Source + "\n$result = calculate(calculate(1, 2.5, \"x\"), \n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(
            session,
            uri,
            PositionAfter(source, "calculate(calculate(1, 2.5, \"x\"), "));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("calculate");
        help.ActiveParameter.Should().Be(1);
    }

    [Fact]
    public async Task SignatureHelp_OwnParameterList_ReturnsNull()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-own-params.tyhp");
        string source = "<?tyhp\n\nfunction calculate(int $a, float $b, \n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(
            session,
            uri,
            PositionAfter(source, "function calculate(int $a, float $b, "));
        help.Should().BeNull();
    }

    [Fact]
    public async Task SignatureHelp_OwnCompleteParameterList_ReturnsNull()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-own-complete-params.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        SignatureHelp? help = await RequestSignatureHelpAsync(
            session,
            uri,
            PositionAfter(Source, "function calculate(int $a, "));
        help.Should().BeNull();
    }

    [Fact]
    public async Task SignatureHelp_ParenInStringBeforeCall_DoesNotConfuseDetection()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-string-paren.tyhp");
        string source = Source + "\n$note = \"look (here)\";\n$result = calculate(\n";
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(
            session,
            uri,
            PositionAfter(source, "calculate("));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("calculate");
        help.ActiveParameter.Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_ConstructorCall_InheritedFromParent_ResolvesParentConstructor()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-ctor-inherited.tyhp");
        string source = Source + """

            class SpecialAdder extends Adder {
                public function extra(): int {
                    return 1;
                }
            }

            $a = new SpecialAdder(
            """;
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "new SpecialAdder("));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("__construct");
        help.Signatures[0].Parameters.Should().HaveCount(1);
    }

    [Fact]
    public async Task SignatureHelp_RecursiveCallInsideOwnBody_StillResolves()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-recursive.tyhp");
        string source = """
            <?tyhp

            function factorial(int $n): int {
                return $n <= 1 ? 1 : $n * factorial($n - 1);
            }
            """;
        await OpenAndWaitAsync(session, uri, source);

        SignatureHelp? help = await RequestSignatureHelpAsync(session, uri, PositionAfter(source, "$n * factorial("));
        help.Should().NotBeNull();
        help!.Signatures[0].Label.Should().Contain("factorial");
        help.ActiveParameter.Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_OutsideCall_ReturnsNull()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-none.tyhp");
        await OpenAndWaitAsync(session, uri, Source);

        SignatureHelp? help = await RequestSignatureHelpAsync(
            session,
            uri,
            new Position { Line = 1, Character = 0 });
        help.Should().BeNull();
    }

    [Fact]
    public async Task SignatureHelp_OverloadedFunction_ReturnsMultipleSignatures()
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri("sig-overload.tyhpdef");
        const string source = """
            <?tyhpdef
            function overloaded(int $a): void;
            function overloaded(string $a, int $b): void;
            """;
        await OpenAndWaitAsync(session, uri, source);

        const string callFile = """
            <?tyhp
            overloaded(
            """;
        Uri callUri = FileUri("sig-overload-call.tyhp");
        await OpenAndWaitAsync(session, callUri, callFile);

        SignatureHelp? help = await RequestSignatureHelpAsync(
            session,
            callUri,
            PositionAfter(callFile, "overloaded("));
        help.Should().NotBeNull();
        help!.Signatures.Should().HaveCountGreaterThanOrEqualTo(2);
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

    private static Task<SignatureHelp?> RequestSignatureHelpAsync(LspTestSession session, Uri uri, Position position)
    {
        return session.Client.InvokeWithParameterObjectAsync<SignatureHelp?>(
            Methods.TextDocumentSignatureHelpName,
            new SignatureHelpParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = position,
            });
    }

    private static string ParameterLabel(ParameterInformation parameter)
    {
        if (parameter.Label.TryGetFirst(out string? text) && !string.IsNullOrEmpty(text))
        {
            return text;
        }

        return parameter.Label.Value?.ToString() ?? string.Empty;
    }

    private static Position PositionAfter(string source, string needle)
    {
        int index = source.LastIndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        return PositionUtilities.GetPosition(source, index + needle.Length);
    }

    private static Uri FileUri(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "tyhp-signature-tests", fileName);
        return new Uri(path);
    }
}
