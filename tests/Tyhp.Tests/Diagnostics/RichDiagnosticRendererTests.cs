using Microsoft.Extensions.Localization;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class RichDiagnosticRendererTests : IDisposable
{
    private sealed class FakeLocalizer : IStringLocalizer<TyhpHostedService>
    {
        private readonly Dictionary<string, string> _templates = new(StringComparer.Ordinal)
        {
            ["CLI_DiagnosticLocationArrow"] = "  --> {0}:{1}:{2}",
            ["CLI_DiagnosticGutterEmpty"] = "{0} |",
            ["CLI_DiagnosticGutterLine"] = "{0} | {1}",
            ["CLI_DiagnosticGutterUnderline"] = "{0} | {1}",
            ["CLI_DiagnosticHelp"] = "   = help: {0}",
            ["CLI_DiagnosticDidYouMean"] = "did you mean `{0}`?",
            ["CLI_DiagnosticNote"] = "   = note: {0}",
            ["ERROR_TYHP1001"] = "unknown error",
            ["error"] = "error",
        };

        public LocalizedString this[string name]
            => this._templates.TryGetValue(name, out var value)
                ? new LocalizedString(name, value)
                : new LocalizedString(name, name);

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => this._templates.Select(kv => new LocalizedString(kv.Key, kv.Value));

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    public RichDiagnosticRendererTests()
    {
        Message.SetLocalizer(new FakeLocalizer());
    }

    // Message.SetLocalizer is process-wide state; leaving the stub installed makes every later
    // test in the run read stub text (or bare resource keys) instead of the real catalog.
    public void Dispose() => Message.ResetLocalizer();

    [Fact]
    public void BuildSnippetLines_RendersPrimaryCaretAndSecondaryLabel()
    {
        var sourceLines = new Dictionary<int, string>
        {
            [2] = "    let x = missing;",
            [5] = "fn missing() {}",
        };

        var diagnostic = Diagnostic.Error(
                MessageCode.ParserUnknownError,
                "src/a.tyhp",
                line: 2,
                column: 12,
                Array.Empty<object>(),
                endLine: 2,
                endColumn: 19)
            .WithLabels(new DiagnosticLabel(
                new DiagnosticSpan("src/a.tyhp", 5, 3, 5, 10),
                "defined here"))
            .WithHelp("declare the symbol before use")
            .WithNote("name lookup is case-sensitive");

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (file, line) => file == "src/a.tyhp" && sourceLines.TryGetValue(line, out var text) ? text : null);

        lines.Should().Contain(l => l.Contains("--> src/a.tyhp:2:12", StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains("let x = missing;", StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains("^^^^^^^", StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains("fn missing() {}", StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains("-------", StringComparison.Ordinal) && l.Contains("defined here", StringComparison.Ordinal));
        lines.Should().Contain("   = help: declare the symbol before use");
        lines.Should().Contain("   = note: name lookup is case-sensitive");
    }

    [Fact]
    public void BuildSnippetLines_WithoutSource_StillEmitsHelpAndNote()
    {
        var diagnostic = Diagnostic.Error(
                MessageCode.ParserUnknownError,
                "missing.tyhp",
                1,
                0,
                Array.Empty<object>())
            .WithHelp("check the path")
            .WithNote("file was not found");

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (_, _) => null);

        lines.Should().Equal(
            "   = help: check the path",
            "   = note: file was not found");
    }

    [Fact]
    public void BuildSnippetLines_Suggestion_SurfacesAsHelpHint()
    {
        var diagnostic = Diagnostic.Error(
                MessageCode.BinderSymbolNotFound,
                "a.tyhp",
                1,
                0,
                ["Useer"])
            .WithSuggestion(DiagnosticSuggestion.Create(
                new DiagnosticSpan("a.tyhp", 1, 0, 1, 5),
                "User",
                "did you mean `User`?"));

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (_, _) => null);

        lines.Should().Contain("   = help: did you mean `User`?");
    }

    [Fact]
    public void BuildSnippetLines_Suggestion_DoesNotDuplicateMatchingHelp()
    {
        var hint = "did you mean `User`?";
        var diagnostic = Diagnostic.Error(
                MessageCode.BinderSymbolNotFound,
                "a.tyhp",
                1,
                0,
                ["Useer"])
            .WithHelp(hint)
            .WithSuggestion(DiagnosticSuggestion.Create(
                new DiagnosticSpan("a.tyhp", 1, 0, 1, 5),
                "User",
                hint));

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (_, _) => null);

        lines.Should().ContainSingle(l => l.Contains("did you mean `User`?", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSnippetLines_SuggestionWithoutDescription_UsesDidYouMeanTemplate()
    {
        var diagnostic = Diagnostic.Error(
                MessageCode.BinderSymbolNotFound,
                "a.tyhp",
                1,
                0,
                ["Useer"])
            .WithSuggestion(DiagnosticSuggestion.Create(
                new DiagnosticSpan("a.tyhp", 1, 0, 1, 5),
                "User"));

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (_, _) => null);

        lines.Should().Contain("   = help: did you mean `User`?");
    }

    [Fact]
    public void BuildSnippetLines_EmptyWhenNoSourceAndNoAnnotations()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "missing.tyhp",
            1,
            0,
            Array.Empty<object>());

        var lines = RichDiagnosticRenderer.BuildSnippetLines(diagnostic, (_, _) => null);
        lines.Should().BeEmpty();
    }

    [Fact]
    public void BuildSnippetLines_TabIndentedSource_AlignsCaretWithExpandedTabs()
    {
        // Two tabs (tab stop 4) render as eight spaces, so the caret has to start at column 8.
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            line: 1,
            column: 2,
            Array.Empty<object>(),
            endLine: 1,
            endColumn: 5);

        var lines = RichDiagnosticRenderer.BuildSnippetLines(diagnostic, (_, _) => "\t\tlet x = 1;");

        var indent = new string(' ', 8);
        lines.Should().Contain("   1 | " + indent + "let x = 1;");
        lines.Should().Contain("     | " + indent + "^^^");
    }

    [Fact]
    public void BuildSnippetLines_EndColumnPastEndOfLine_ClampsUnderline()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            line: 1,
            column: 0,
            Array.Empty<object>(),
            endLine: 1,
            endColumn: 500);

        var lines = RichDiagnosticRenderer.BuildSnippetLines(diagnostic, (_, _) => "abc");

        lines.Should().Contain("     | ^^^");
    }

    [Fact]
    public void BuildSnippetLines_LongSpan_RendersOnlyFirstAndLastLine()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            line: 1,
            column: 0,
            Array.Empty<object>(),
            endLine: 40,
            endColumn: 1);

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (_, line) => $"line {line}");

        lines.Should().Contain(l => l.Contains("| line 1", StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains("| line 40", StringComparison.Ordinal));
        lines.Should().NotContain(l => l.Contains("| line 20", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSnippetLines_WideLineNumbers_KeepGutterBarsAligned()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            line: 12345,
            column: 0,
            Array.Empty<object>(),
            endLine: 12345,
            endColumn: 3);

        var lines = RichDiagnosticRenderer.BuildSnippetLines(diagnostic, (_, _) => "abc");

        var barColumns = lines
            .Where(l => l.Contains('|', StringComparison.Ordinal))
            .Select(l => l.IndexOf('|', StringComparison.Ordinal))
            .Distinct();

        barColumns.Should().ContainSingle();
    }

    [Fact]
    public void BuildSnippet_ClassifiesSourceLinesStartingWithDashAsSource()
    {
        var diagnostic = Diagnostic.Error(
                MessageCode.ParserUnknownError,
                "a.tyhp",
                line: 1,
                column: 0,
                Array.Empty<object>(),
                endLine: 1,
                endColumn: 2)
            .WithHelp("keep going");

        var snippet = RichDiagnosticRenderer.BuildSnippet(diagnostic, (_, _) => "-- a comment");

        snippet.Single(l => l.Text.Contains("-- a comment", StringComparison.Ordinal))
            .IsAnnotation.Should().BeFalse();
        snippet.Single(l => l.Text.Contains("^^", StringComparison.Ordinal))
            .IsAnnotation.Should().BeTrue();
        snippet.Single(l => l.Text.Contains("help", StringComparison.Ordinal))
            .IsAnnotation.Should().BeTrue();
    }

    [Fact]
    public void ConsoleDiagnosticFormatter_Quiet_DoesNotThrow()
    {
        var formatter = new ConsoleDiagnosticFormatter(quiet: true);
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            1,
            0,
            Array.Empty<object>());

        var action = () => formatter.Format(diagnostic);
        action.Should().NotThrow();
    }

    [Fact]
    public void CreateFormatter_TextQuiet_ReturnsQuietConsoleFormatter()
    {
        var formatter = LintAction.CreateFormatter("text", quiet: true);
        formatter.Should().BeOfType<ConsoleDiagnosticFormatter>();
        ((ConsoleDiagnosticFormatter)formatter).Quiet.Should().BeTrue();
    }

    [Fact]
    public void Diagnostic_WithLabels_PreservesPrimaryAndAddsSecondary()
    {
        var diagnostic = Diagnostic.Error(
                MessageCode.ParserUnknownError,
                "a.tyhp",
                3,
                4,
                Array.Empty<object>(),
                endLine: 3,
                endColumn: 8)
            .WithLabels(new DiagnosticLabel(new DiagnosticSpan("a.tyhp", 1, 0, 1, 3), "here"));

        diagnostic.PrimarySpan.Line.Should().Be(3);
        diagnostic.PrimarySpan.EndColumn.Should().Be(8);
        diagnostic.Labels.Should().HaveCount(1);
        diagnostic.Labels[0].Message.Should().Be("here");
    }
}
