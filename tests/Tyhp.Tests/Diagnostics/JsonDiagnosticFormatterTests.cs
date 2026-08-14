using System.Text.Json;
using Microsoft.Extensions.Localization;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class JsonDiagnosticFormatterTests : IDisposable
{
    private sealed class FakeLocalizer : IStringLocalizer<TyhpHostedService>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => Enumerable.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    public JsonDiagnosticFormatterTests()
    {
        Message.SetLocalizer(new FakeLocalizer());
    }

    // Message.SetLocalizer is process-wide state; leaving the stub installed makes every later
    // test in the run read bare resource keys instead of the real catalog.
    public void Dispose() => Message.ResetLocalizer();

    [Fact]
    public void FormatSummary_EmptyBag_EmitsValidDocumentWithEmptyDiagnostics()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer, prettyPrint: false);
        var bag = new DiagnosticBag();
        var result = new CompilationResult { SourceFileCount = 0 };

        formatter.SetContext(result);
        formatter.FormatSummary(bag);

        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        root.GetProperty("version").GetString().Should().Be("1.0");
        root.GetProperty("tool").GetProperty("name").GetString().Should().Be("tyhp");
        root.GetProperty("tool").GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("diagnostics").GetArrayLength().Should().Be(0);

        var summary = root.GetProperty("summary");
        summary.GetProperty("filesChecked").GetInt32().Should().Be(0);
        summary.GetProperty("errorCount").GetInt32().Should().Be(0);
        summary.GetProperty("warningCount").GetInt32().Should().Be(0);
        summary.GetProperty("infoCount").GetInt32().Should().Be(0);
        summary.GetProperty("durations").GetProperty("totalMs").GetInt64().Should().Be(0);
    }

    [Fact]
    public void Format_DoesNotWriteUntilFormatSummary()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "src/a.tyhp",
            42,
            8,
            Array.Empty<object>());

        formatter.Format(diagnostic);
        writer.ToString().Should().BeEmpty();

        formatter.FormatSummary(new DiagnosticBag());
        writer.ToString().Should().NotBeNullOrWhiteSpace();
        JsonDocument.Parse(writer.ToString());
    }

    [Fact]
    public void FormatSummary_ConvertsLineToZeroBased_AndPassesColumnThrough()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        var diagnostic = Diagnostic.Error(
            MessageCode.CheckerTypeMismatch,
            "src/Models/User.tyhp",
            line: 42,
            column: 8,
            Array.Empty<object>(),
            endLine: 42,
            endColumn: 25);
        bag.Add(diagnostic);

        var result = new CompilationResult
        {
            SourceFileCount = 3,
            ParseDuration = TimeSpan.FromMilliseconds(1200),
            BindDuration = TimeSpan.FromMilliseconds(800),
            CheckDuration = TimeSpan.FromMilliseconds(500),
        };

        formatter.SetContext(result);
        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var diag = doc.RootElement.GetProperty("diagnostics")[0];

        diag.GetProperty("severity").GetString().Should().Be("error");
        diag.GetProperty("code").GetString().Should().Be($"TYHP{(int)MessageCode.CheckerTypeMismatch:D4}");
        diag.GetProperty("file").GetString().Should().Be("src/Models/User.tyhp");

        var start = diag.GetProperty("range").GetProperty("start");
        start.GetProperty("line").GetInt32().Should().Be(41);
        start.GetProperty("column").GetInt32().Should().Be(8);

        var end = diag.GetProperty("range").GetProperty("end");
        end.GetProperty("line").GetInt32().Should().Be(41);
        end.GetProperty("column").GetInt32().Should().Be(25);

        var summary = doc.RootElement.GetProperty("summary");
        summary.GetProperty("filesChecked").GetInt32().Should().Be(3);
        summary.GetProperty("errorCount").GetInt32().Should().Be(1);
        summary.GetProperty("durations").GetProperty("parseMs").GetInt64().Should().Be(1200);
        summary.GetProperty("durations").GetProperty("bindMs").GetInt64().Should().Be(800);
        summary.GetProperty("durations").GetProperty("checkMs").GetInt64().Should().Be(500);
        summary.GetProperty("durations").GetProperty("totalMs").GetInt64().Should().Be(2500);
    }

    [Fact]
    public void FormatSummary_SingleFileMode_IncludesFileInSummary()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        var result = new CompilationResult
        {
            SourceFileCount = 1,
            LintTargetFile = "src/User.tyhp",
        };

        formatter.SetContext(result);
        formatter.FormatSummary(bag);

        using var doc = JsonDocument.Parse(writer.ToString());
        var summary = doc.RootElement.GetProperty("summary");
        summary.GetProperty("filesChecked").GetInt32().Should().Be(1);
        summary.GetProperty("file").GetString().Should().Be("src/User.tyhp");
    }

    [Fact]
    public void FormatSummary_WithoutLintTargetFile_OmitsFileProperty()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        var result = new CompilationResult { SourceFileCount = 2 };

        formatter.SetContext(result);
        formatter.FormatSummary(bag);

        using var doc = JsonDocument.Parse(writer.ToString());
        doc.RootElement.GetProperty("summary").TryGetProperty("file", out _).Should().BeFalse();
    }

    [Fact]
    public void Format_WhenEndRangeMissing_EndEqualsStart()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Warning(
            MessageCode.ParserUnknownError,
            "b.tyhp",
            line: 1,
            column: 0,
            Array.Empty<object>()));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var range = doc.RootElement.GetProperty("diagnostics")[0].GetProperty("range");
        var start = range.GetProperty("start");
        var end = range.GetProperty("end");

        start.GetProperty("line").GetInt32().Should().Be(0);
        start.GetProperty("column").GetInt32().Should().Be(0);
        end.GetProperty("line").GetInt32().Should().Be(0);
        end.GetProperty("column").GetInt32().Should().Be(0);
    }

    [Fact]
    public void FormatSummary_ClampsInvalidLineToZero()
    {
        // Bypass Diagnostic constructor clamping by using a custom stub.
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        formatter.Format(new StubDiagnostic(line: 0, column: -3));
        formatter.FormatSummary(new DiagnosticBag());

        using var doc = JsonDocument.Parse(writer.ToString());
        var start = doc.RootElement.GetProperty("diagnostics")[0].GetProperty("range").GetProperty("start");
        start.GetProperty("line").GetInt32().Should().Be(0);
        start.GetProperty("column").GetInt32().Should().Be(0);
    }

    [Fact]
    public void FormatSummary_IncludesLabelsHelpNoteAndSuggestion()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();

        var diagnostic = Diagnostic.Error(
                MessageCode.CheckerTypeMismatch,
                "src/a.tyhp",
                line: 10,
                column: 4,
                Array.Empty<object>(),
                endLine: 10,
                endColumn: 9)
            .WithLabels(new DiagnosticLabel(
                new DiagnosticSpan("src/a.tyhp", 2, 0, 2, 5),
                "defined here"))
            .WithHelp("convert the value")
            .WithNote("expected int")
            .WithSuggestion(new DiagnosticSuggestion(
                new DiagnosticSpan("src/a.tyhp", 10, 4, 10, 9),
                "42",
                "use an integer literal"));

        bag.Add(diagnostic);
        formatter.SetContext(new CompilationResult { SourceFileCount = 1 });
        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var diag = doc.RootElement.GetProperty("diagnostics")[0];

        diag.GetProperty("help").GetString().Should().Be("convert the value");
        diag.GetProperty("note").GetString().Should().Be("expected int");

        var label = diag.GetProperty("labels")[0];
        label.GetProperty("message").GetString().Should().Be("defined here");
        label.GetProperty("file").GetString().Should().Be("src/a.tyhp");
        label.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(1);
        label.GetProperty("range").GetProperty("start").GetProperty("column").GetInt32().Should().Be(0);
        label.GetProperty("range").GetProperty("end").GetProperty("column").GetInt32().Should().Be(5);

        var suggestion = diag.GetProperty("suggestion");
        suggestion.GetProperty("replacement").GetString().Should().Be("42");
        suggestion.GetProperty("description").GetString().Should().Be("use an integer literal");
        suggestion.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(9);
        suggestion.GetProperty("range").GetProperty("end").GetProperty("column").GetInt32().Should().Be(9);
    }

    [Fact]
    public void FormatSummary_OmitsOptionalFieldsWhenAbsent()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            1,
            0,
            Array.Empty<object>()));

        formatter.SetContext(new CompilationResult { SourceFileCount = 1 });
        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var diag = doc.RootElement.GetProperty("diagnostics")[0];
        diag.TryGetProperty("labels", out _).Should().BeFalse();
        diag.TryGetProperty("help", out _).Should().BeFalse();
        diag.TryGetProperty("note", out _).Should().BeFalse();
        diag.TryGetProperty("suggestion", out _).Should().BeFalse();
    }

    [Fact]
    public void FormatSummary_NullBag_DoesNotThrow()
    {
        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var action = () => formatter.FormatSummary(null!);
        action.Should().NotThrow();
        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ConsoleDiagnosticFormatter_StillFormatsWithoutSetContext()
    {
        var formatter = new ConsoleDiagnosticFormatter();
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);

        var action = () => bag.DisplayAll(formatter);
        action.Should().NotThrow();
    }

    private sealed class StubDiagnostic : IDiagnostic
    {
        public StubDiagnostic(int line, int column)
        {
            this.Line = line;
            this.Column = column;
        }

        public DiagnosticSeverity Severity => DiagnosticSeverity.Info;
        public MessageCode Code => MessageCode.ParserUnknownError;
        public string FileName => "stub.tyhp";
        public int Line { get; }
        public int Column { get; }
        public int? EndLine => null;
        public int? EndColumn => null;
        public string Message => "stub";
        public object[] FormatParams => Array.Empty<object>();
        public IReadOnlyList<DiagnosticLabel> Labels => Array.Empty<DiagnosticLabel>();
        public string? Help => null;
        public string? Note => null;
        public DiagnosticSuggestion? Suggestion => null;
        public void Display(IDiagnosticFormatter? formatter = null) => formatter?.Format(this);
    }
}
