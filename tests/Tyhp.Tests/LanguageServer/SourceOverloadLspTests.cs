using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.LanguageServer;

/// <summary>
/// Hover, signature help and completion details for <c>.tyhp</c> overload sets show the declared
/// overload signatures; the implementation signature is not part of what callers can use.
/// </summary>
[Trait("Category", "LanguageServer")]
public class SourceOverloadLspTests
{
    private const string Source = """
        <?tyhp

        function area(int $r): float;
        function area(int $w, int $h): float;
        function area(int|string $a, int $b = 0): float {
            return 1.0;
        }

        final class Shape {
            public function scale(int $by): int;
            public function scale(string $by, bool $flag): string;
            public function scale(int|string $by, bool $flag = false): int|string {
                return $by;
            }
        }

        function demo(Shape $shape): void {
            float $a = area(1);
            $b = $shape->scale(2);
        }
        """;

    // ---- hover ----

    [Fact]
    public async Task Hover_OnOverloadedFunctionCall_ShowsDeclaredSignaturesNotImplementation()
    {
        string text = await HoverAsync("hover-fn-call.tyhp", Source, "area(1)", 0);

        text.Should().Contain("function area(int $r): float");
        text.Should().Contain("function area(int $w, int $h): float");
        text.Should().NotContain("int|string");
    }

    [Fact]
    public async Task Hover_OnOverloadedMethodCall_ShowsDeclaredSignaturesNotImplementation()
    {
        string text = await HoverAsync("hover-method-call.tyhp", Source, "scale(2)", 0);

        text.Should().Contain("function scale(int $by): int");
        text.Should().Contain("function scale(string $by, bool $flag): string");
        text.Should().NotContain("int|string");
    }

    [Fact]
    public async Task Hover_OnImplementationName_ShowsDeclaredSignatures()
    {
        string text = await HoverAsync("hover-impl.tyhp", Source, "function area(int|string", "function ".Length);

        text.Should().Contain("function area(int $r): float");
        text.Should().Contain("function area(int $w, int $h): float");
        text.Should().NotContain("int|string");
    }

    [Fact]
    public async Task Hover_OnOverloadSignatureName_ShowsThatSignature()
    {
        string text = await HoverAsync("hover-sig.tyhp", Source, "function area(int $w", "function ".Length);

        text.Should().Contain("function area(int $w, int $h): float");
        text.Should().NotContain("int|string");
    }

    // ---- signature help ----

    [Fact]
    public async Task SignatureHelp_OnOverloadedFunction_ListsDeclaredSignatures()
    {
        string source = Source + "\n$x = area();\n";
        SignatureHelp help = await SignatureHelpAfterAsync("sig-fn.tyhp", source, "area(");

        help.Signatures.Should().HaveCount(2);
        help.Signatures.Select(signature => signature.Label).Should().NotContain(label => label.Contains("int|string"));
        help.Signatures[0].Label.Should().Contain("int $r");
        help.Signatures[1].Label.Should().Contain("int $w, int $h");
    }

    [Fact]
    public async Task SignatureHelp_OnOverloadedMethod_ListsDeclaredSignatures()
    {
        string source = Source + "\nfunction probe(Shape $s): void {\n    $y = $s->scale();\n}\n";
        SignatureHelp help = await SignatureHelpAfterAsync("sig-method.tyhp", source, "$s->scale(");

        help.Signatures.Should().HaveCount(2);
        help.Signatures.Select(signature => signature.Label).Should().NotContain(label => label.Contains("int|string"));
        help.Signatures[0].Label.Should().Contain("int $by");
        help.Signatures[1].Label.Should().Contain("string $by, bool $flag");
    }

    // ---- completion detail ----

    [Fact]
    public void CompletionDetail_OfOverloadedFunction_ShowsFirstSignatureAndOverloadCount()
    {
        FunctionDeclarationSymbol area = FindFunctionAt(Source, line: 5);

        string detail = SymbolFormatter.FormatCompletionDetail(area);

        detail.Should().Be("function area(int $r): float (+1 overloads)");
    }

    [Fact]
    public void CompletionDetail_OfPlainFunction_IsItsSignature()
    {
        FunctionDeclarationSymbol plain = FindFunctionAt("<?tyhp\nfunction plain(int $x): int { return $x; }\n", line: 2);

        SymbolFormatter.FormatCompletionDetail(plain).Should().Be(SymbolFormatter.FormatSignature(plain));
    }

    [Fact]
    public void FormatHover_OnOverloadSet_UsesFirstDocumentedSignature()
    {
        FunctionDeclarationSymbol twice = FindFunctionAt("""
            <?tyhp
            /** Doubles a number. */
            function twice(int $x): int;
            function twice(string $x): string;
            function twice(int|string $x): int|string {
                return $x;
            }
            """, line: 5);

        string hover = SymbolFormatter.FormatHover(twice);

        hover.Should().Contain("function twice(int $x): int\nfunction twice(string $x): string");
        hover.Should().Contain("Doubles a number.");
    }

    // ---- helpers ----

    private static async Task<string> HoverAsync(string fileName, string source, string needle, int innerOffset)
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri(fileName);
        await OpenAndWaitAsync(session, uri, source);

        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        Hover? hover = await session.Client.InvokeWithParameterObjectAsync<Hover?>(
            Methods.TextDocumentHoverName,
            new TextDocumentPositionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = PositionUtilities.GetPosition(source, index + innerOffset),
            });
        hover.Should().NotBeNull();
        return HoverText(hover!);
    }

    private static async Task<SignatureHelp> SignatureHelpAfterAsync(string fileName, string source, string needle)
    {
        await using var session = await LspTestSession.StartAsync();
        await session.InitializeAsync();
        Uri uri = FileUri(fileName);
        await OpenAndWaitAsync(session, uri, source);

        int index = source.LastIndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"needle '{needle}' should exist");
        SignatureHelp? help = await session.Client.InvokeWithParameterObjectAsync<SignatureHelp?>(
            Methods.TextDocumentSignatureHelpName,
            new SignatureHelpParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = PositionUtilities.GetPosition(source, index + needle.Length),
            });
        help.Should().NotBeNull();
        return help!;
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
        string path = Path.Combine(Path.GetTempPath(), "tyhp-source-overload-lsp-tests", fileName);
        return new Uri(path);
    }

    /// <summary>The function symbol declared on the 1-based <paramref name="line"/> (name starts at column 9).</summary>
    private static FunctionDeclarationSymbol FindFunctionAt(string content, int line)
    {
        using var compilation = new CompilationService();
        var diagnostics = new DiagnosticBag();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            ProjectPath = Path.GetTempPath(),
            SkipChecking = true,
        };
        SrcFileAst? ast = compilation.ParseFromContent(content, "source-overload-lsp.tyhp", diagnostics, options);
        ast.Should().NotBeNull();
        var binder = new TyhpBinder(diagnostics, options);
        GlobalScope? scope = binder.Bind([ast!]);
        scope.Should().NotBeNull();

        BaseSymbol? symbol = new SymbolFinder().FindSymbolAtPosition(ast!, scope, line, column: 9);
        symbol.Should().BeOfType<FunctionDeclarationSymbol>();
        return (FunctionDeclarationSymbol)symbol!;
    }
}
