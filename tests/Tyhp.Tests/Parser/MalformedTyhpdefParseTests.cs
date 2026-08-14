using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Regression for Story 13 Phase 7 #1 — malformed tyhpdef must yield diagnostics, not NRE.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class MalformedTyhpdefParseTests
{
    [Theory]
    [InlineData("<?tyhpdef")]
    [InlineData("<?tyhpdef\n@@@ garbage !!!")]
    [InlineData("<?tyhpdef\nclass {")]
    [InlineData("<?tyhpdef\nfunction foo(")]
    [InlineData("<?tyhpdef\nconst")]
    [InlineData("<?tyhpdef\nclass Foo { public int $x")]
    [InlineData("<?tyhpdef\nnamespace")]
    [InlineData("<?tyhpdef\nuse")]
    [InlineData("not even a tag")]
    [InlineData("")]
    [InlineData("<?tyhpdef\nclass Foo {\n    public function bar(\n")]
    [InlineData("<?tyhpdef\ninterface I {\n    function m(): void\n")]
    [InlineData("<?tyhpdef\nenum E { case A")]
    [InlineData("<?tyhpdef\nfunction f(): void")]
    [InlineData("<?tyhpdef\ntrait T {")]
    public void ParseContent_Malformed_DoesNotThrow_ReportsDiagnostics(string content)
    {
        AstCacheService.Clear();
        try
        {
            var diagnostics = new DiagnosticBag();
            var act = () => Tyhpdef.ParseContent(content, "bad.tyhpdef", ParseMode.Tyhpdef, diagnostics);
            act.Should().NotThrow(
                "malformed tyhpdef must not escape as NullReferenceException from the visitor");

            // Empty / tag-only may recover to an empty tree; garbage with tokens must surface errors.
            if (!string.IsNullOrWhiteSpace(content) && content != "<?tyhpdef")
            {
                diagnostics.HasErrors.Should().BeTrue(
                    "broken tyhpdef content should produce parse/visitor diagnostics, not a silent success");
            }
        }
        finally
        {
            AstCacheService.Clear();
        }
    }

    [Fact]
    public void ParseContent_GarbageAfterOpenTag_DoesNotThrowAndSurfacesDiagnostics()
    {
        AstCacheService.Clear();
        try
        {
            // Matches the integrity-check fixture shape from FOUND_BUGS Story 13 Phase 7 #1.
            const string content = "<?tyhpdef\nthis is clearly not valid tyhpdef !!!\n";
            var diagnostics = new DiagnosticBag();

            var act = () => Tyhpdef.ParseContent(content, "bad.tyhpdef", ParseMode.Tyhpdef, diagnostics);
            act.Should().NotThrow();

            diagnostics.HasErrors.Should().BeTrue();
            var accepted = new HashSet<MessageCode>
            {
                MessageCode.TyhpdefParseError,
                MessageCode.VisitorMissingRequiredNode,
                MessageCode.VisitorUnexpectedAlternative,
                MessageCode.ParserCompileAborted,
                MessageCode.ParserUnexpectedError,
                MessageCode.ParserUnknownError,
            };
            diagnostics.Errors.Select(d => d.Code).Should().Contain(code => accepted.Contains(code));
        }
        finally
        {
            AstCacheService.Clear();
        }
    }
}
