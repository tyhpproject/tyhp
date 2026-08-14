using System.Text.Json;
using Microsoft.Extensions.Localization;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class SarifDiagnosticFormatterTests : IDisposable
{
    private sealed class FakeLocalizer : IStringLocalizer<TyhpHostedService>
    {
        private readonly Dictionary<string, string> _templates = new(StringComparer.Ordinal)
        {
            ["ERROR_TYHP4002"] = "`{0}` cannot have multiple visibility modifiers",
            // No placeholders — Diagnostic.Message formats with whatever FormatParams the test supplies.
            ["WARNING_TYHP1001"] = "Warning template",
            ["INFO_TYHP1001"] = "Info template",
            // Backticked spans where the placeholder is not the whole span.
            ["ERROR_TYHP4013"] = "Variable `${0}` is used before being assigned",
            ["WARNING_TYHP8026"] = "PHP extension package not found; install `tyhp/php-{0}` via Composer",
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

    public SarifDiagnosticFormatterTests()
    {
        Message.SetLocalizer(new FakeLocalizer());
    }

    // Message.SetLocalizer is process-wide state; leaving the stub installed makes every later
    // test in the run read this fixture's templates instead of the real catalog.
    public void Dispose() => Message.ResetLocalizer();

    [Fact]
    public void FormatSummary_EmptyBag_EmitsValidSarifWithEmptyResults()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer, prettyPrint: false);

        formatter.FormatSummary(new DiagnosticBag());

        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        root.GetProperty("$schema").GetString().Should().Contain("sarif-schema-2.1.0");
        root.GetProperty("version").GetString().Should().Be("2.1.0");

        var run = root.GetProperty("runs")[0];
        var driver = run.GetProperty("tool").GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be("tyhp");
        driver.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        driver.GetProperty("informationUri").GetString().Should().Be("https://tyhp.dev");
        driver.GetProperty("rules").GetArrayLength().Should().Be(0);
        run.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Format_DoesNotWriteUntilFormatSummary()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var diagnostic = Diagnostic.Error(
            MessageCode.CheckerMultipleVisibilities,
            "src/a.tyhp",
            42,
            8,
            ["property"]);

        formatter.Format(diagnostic);
        writer.ToString().Should().BeEmpty();

        formatter.FormatSummary(new DiagnosticBag());
        writer.ToString().Should().NotBeNullOrWhiteSpace();
        JsonDocument.Parse(writer.ToString());
    }

    [Fact]
    public void FormatSummary_MapsCoordinatesToOneBasedLineAndColumn()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(
            MessageCode.CheckerMultipleVisibilities,
            "src/Models/User.tyhp",
            line: 42,
            column: 8,
            ["property"],
            endLine: 42,
            endColumn: 25));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var result = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];

        result.GetProperty("ruleId").GetString().Should().Be(
            $"TYHP{(int)MessageCode.CheckerMultipleVisibilities:D4}");
        result.GetProperty("level").GetString().Should().Be("error");
        result.GetProperty("message").GetProperty("text").GetString().Should().Contain("visibility");

        var region = result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region");
        region.GetProperty("startLine").GetInt32().Should().Be(42);
        region.GetProperty("startColumn").GetInt32().Should().Be(9); // 0-based 8 → 1-based 9
        region.GetProperty("endLine").GetInt32().Should().Be(42);
        region.GetProperty("endColumn").GetInt32().Should().Be(26); // 0-based 25 → 1-based 26

        var uri = result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString();
        uri.Should().Be("src/Models/User.tyhp");
        uri.Should().NotContain("\\");
    }

    [Fact]
    public void FormatSummary_CollectsUniqueRules_AndMapsSeverities()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();

        bag.Add(Diagnostic.Error(
            MessageCode.CheckerMultipleVisibilities,
            "a.tyhp",
            1,
            0,
            ["property"]));
        bag.Add(Diagnostic.Error(
            MessageCode.CheckerMultipleVisibilities,
            "b.tyhp",
            2,
            0,
            ["method"]));
        bag.Add(Diagnostic.Warning(
            MessageCode.ParserUnknownError,
            "c.tyhp",
            3,
            0,
            Array.Empty<object>()));
        bag.Add(Diagnostic.Info(
            MessageCode.ParserUnknownError,
            "d.tyhp",
            4,
            0,
            Array.Empty<object>()));
        bag.Add(new Diagnostic(
            DiagnosticSeverity.Hint,
            MessageCode.ParserUnknownError,
            "e.tyhp",
            5,
            0,
            Array.Empty<object>()));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var run = doc.RootElement.GetProperty("runs")[0];
        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");
        var results = run.GetProperty("results");

        results.GetArrayLength().Should().Be(5);
        // Unique rule IDs: TYHP4002 + TYHP1001 (warning/info/hint share the same code)
        rules.GetArrayLength().Should().Be(2);

        var levels = results.EnumerateArray()
            .Select(r => r.GetProperty("level").GetString())
            .ToList();
        levels.Should().ContainInOrder("error", "error", "warning", "note", "note");

        var errorRule = rules.EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() ==
                $"TYHP{(int)MessageCode.CheckerMultipleVisibilities:D4}");
        errorRule.GetProperty("defaultConfiguration").GetProperty("level").GetString()
            .Should().Be("error");
        errorRule.GetProperty("shortDescription").GetProperty("text").GetString()
            .Should().Be("cannot have multiple visibility modifiers");

        var sharedRule = rules.EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() ==
                $"TYHP{(int)MessageCode.ParserUnknownError:D4}");
        // warning > note — defaultConfiguration keeps the highest severity seen
        sharedRule.GetProperty("defaultConfiguration").GetProperty("level").GetString()
            .Should().Be("warning");
    }

    [Fact]
    public void FormatSummary_OmitsLocations_WhenFileNameEmpty()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(
            MessageCode.CheckerMultipleVisibilities,
            fileName: "",
            line: 1,
            column: 0,
            ["property"]));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var result = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        // Empty artifact URIs are omitted entirely (GitHub Code Scanning rejects uri:"").
        result.TryGetProperty("locations", out _).Should().BeFalse();
    }

    [Fact]
    public void FormatSummary_KeepsAlreadyRelativeUri_Unchanged()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(
            MessageCode.CheckerMultipleVisibilities,
            "src/Nested/File.tyhp",
            10,
            2,
            ["property"]));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var uri = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString();

        uri.Should().Be("src/Nested/File.tyhp");
    }

    [Fact]
    public void FormatSummary_ShortDescriptionFallsBackAcrossSeverityPrefixes()
    {
        // No WARNING_TYHP4002 in the fake localizer — should fall back to ERROR_TYHP4002.
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Warning(
            MessageCode.CheckerMultipleVisibilities,
            "a.tyhp",
            1,
            0,
            ["property"]));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var rule = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules")[0];

        rule.GetProperty("shortDescription").GetProperty("text").GetString()
            .Should().Be("cannot have multiple visibility modifiers");
        rule.GetProperty("defaultConfiguration").GetProperty("level").GetString()
            .Should().Be("warning");
    }

    [Theory]
    [InlineData(
        MessageCode.CheckerMultipleVisibilities,
        "cannot have multiple visibility modifiers")]
    [InlineData(
        MessageCode.CheckerVariableUsedBeforeAssignment,
        "Variable is used before being assigned")]
    [InlineData(
        MessageCode.TyhpdefPhpExtensionPackageNotFound,
        "PHP extension package not found; install via Composer")]
    public void FormatSummary_ShortDescriptionDropsBacktickedPlaceholderSpans(
        MessageCode code,
        string expected)
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(code, "a.tyhp", 1, 0, ["value"]));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var rule = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules")[0];

        rule.GetProperty("shortDescription").GetProperty("text").GetString()
            .Should().Be(expected);
    }

    [Fact]
    public void FormatSummary_UpgradesRuleDefaultLevelToMostSevere()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Info(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            1,
            0,
            Array.Empty<object>()));
        bag.Add(Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "b.tyhp",
            2,
            0,
            Array.Empty<object>()));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var rule = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules")[0];

        rule.GetProperty("defaultConfiguration").GetProperty("level").GetString()
            .Should().Be("error");
    }

    [Fact]
    public void Format_WhenEndRangeMissing_EndEqualsStart()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Warning(
            MessageCode.ParserUnknownError,
            "b.tyhp",
            line: 1,
            column: 0,
            Array.Empty<object>()));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var region = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region");

        region.GetProperty("startLine").GetInt32().Should().Be(1);
        region.GetProperty("startColumn").GetInt32().Should().Be(1);
        region.GetProperty("endLine").GetInt32().Should().Be(1);
        region.GetProperty("endColumn").GetInt32().Should().Be(1);
    }

    [Fact]
    public void FormatSummary_IncludesRelatedLocationsFixesAndProperties()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();

        bag.Add(Diagnostic.Error(
                MessageCode.CheckerMultipleVisibilities,
                "src/a.tyhp",
                line: 10,
                column: 4,
                ["property"],
                endLine: 10,
                endColumn: 9)
            .WithLabels(new DiagnosticLabel(
                new DiagnosticSpan("src/a.tyhp", 2, 0, 2, 5),
                "defined here"))
            .WithHelp("pick one visibility")
            .WithNote("modifiers conflict")
            .WithSuggestion(new DiagnosticSuggestion(
                new DiagnosticSpan("src/a.tyhp", 10, 4, 10, 9),
                "public",
                "keep public")));

        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var result = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];

        var related = result.GetProperty("relatedLocations")[0];
        related.GetProperty("message").GetProperty("text").GetString().Should().Be("defined here");
        related.GetProperty("physicalLocation").GetProperty("region").GetProperty("startLine").GetInt32()
            .Should().Be(2);
        related.GetProperty("physicalLocation").GetProperty("region").GetProperty("startColumn").GetInt32()
            .Should().Be(1); // 0-based 0 → 1-based 1

        var fix = result.GetProperty("fixes")[0];
        fix.GetProperty("description").GetProperty("text").GetString().Should().Be("keep public");
        var replacement = fix.GetProperty("artifactChanges")[0].GetProperty("replacements")[0];
        replacement.GetProperty("insertedContent").GetProperty("text").GetString().Should().Be("public");
        replacement.GetProperty("deletedRegion").GetProperty("startLine").GetInt32().Should().Be(10);
        replacement.GetProperty("deletedRegion").GetProperty("startColumn").GetInt32().Should().Be(5);

        var properties = result.GetProperty("properties");
        properties.GetProperty("help").GetString().Should().Be("pick one visibility");
        properties.GetProperty("note").GetString().Should().Be("modifiers conflict");
    }

    [Fact]
    public void FormatSummary_NullBag_DoesNotThrow()
    {
        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var action = () => formatter.FormatSummary(null!);
        action.Should().NotThrow();
        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public void CreateFormatter_Sarif_ReturnsSarifDiagnosticFormatter()
    {
        LintAction.CreateFormatter("sarif").Should().BeOfType<SarifDiagnosticFormatter>();
        LintAction.UsesMachineReadableOutput("sarif").Should().BeTrue();
        LintAction.UsesMachineReadableOutput("json").Should().BeTrue();
        LintAction.UsesMachineReadableOutput("text").Should().BeFalse();
    }
}
