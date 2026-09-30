using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class ExtensionBlockTargetLspSurfaceTests
{
    [Fact]
    public void SemanticTokens_ColorExtensionHeaderAndNestedTargets()
    {
        const string Source = """
            <?tyhp
            extension StringOps<T> extends string {
                function id(T $value): T { return $value; }
            }

            extension NumericHelpers {
                extends int {
                    function abs(): int { return $this; }
                }

                extends \App\Money {
                    function doubled(): self { return $this; }
                }
            }
            """;

        (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(Source, "ext-tokens.tyhp");
        int[] data = SemanticTokenCollector.CollectData(ast, Source, scope, tree: null, new SymbolFinder());
        IReadOnlyList<DecodedSemanticToken> decoded = SemanticTokenCollector.Decode(data);

        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Type
            && TokenText(Source, token) == "string");
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.TypeParameter
            && TokenText(Source, token) == "T");
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Type
            && TokenText(Source, token) == "int");
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Type
            && TokenText(Source, token) == "Money");
        decoded.Should().Contain(token =>
            token.Type == SemanticTokenTypes.Class
            && TokenText(Source, token) == "NumericHelpers");
    }

    private static (SrcFileAst Ast, GlobalScope? Scope) ParseAndBind(string content, string fileName)
    {
        using var compilation = new CompilationService();
        var diagnostics = new DiagnosticBag();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            ProjectPath = Path.GetTempPath(),
            SkipChecking = true,
        };
        SrcFileAst? ast = compilation.ParseFromContent(content, fileName, diagnostics, options);
        ast.Should().NotBeNull();
        var binder = new TyhpBinder(diagnostics, options);
        GlobalScope? scope = binder.Bind([ast!]);
        return (ast!, scope);
    }

    private static string TokenText(string source, DecodedSemanticToken token)
    {
        Position start = new() { Line = token.Line, Character = token.Character };
        int offset = PositionUtilities.GetOffset(source, start);
        int end = Math.Min(source.Length, offset + token.Length);
        return offset >= 0 && offset < end ? source[offset..end] : string.Empty;
    }
}
