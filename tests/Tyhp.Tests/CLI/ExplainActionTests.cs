using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class ExplainActionTests
{
    [Theory]
    [InlineData("TYHP4008", MessageCode.CheckerTypeMismatch)]
    [InlineData("tyhp4008", MessageCode.CheckerTypeMismatch)]
    [InlineData("4008", MessageCode.CheckerTypeMismatch)]
    [InlineData("3003", MessageCode.BinderSymbolNotFound)]
    [InlineData("TYHP1001", MessageCode.ParserUnknownError)]
    public void TryParseToken_AcceptsPrefixAndBareForms(string token, MessageCode expected)
    {
        MessageCodeCatalog.TryParseToken(token, out var code).Should().BeTrue();
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TYHP")]
    [InlineData("not-a-code")]
    [InlineData("TYHP99999")]
    [InlineData("0")]
    public void TryParseToken_RejectsInvalidTokens(string token)
    {
        MessageCodeCatalog.TryParseToken(token, out _).Should().BeFalse();
    }

    [Fact]
    public void Catalog_IncludesEveryMessageCodeExceptNoError()
    {
        var expected = Enum.GetValues<MessageCode>()
            .Where(c => c != MessageCode.NoError)
            .Select(c => (int)c)
            .OrderBy(n => n)
            .ToList();

        MessageCodeCatalog.All.Select(e => e.NumericCode).Should().Equal(expected);
    }

    [Fact]
    public void Catalog_ShortMessage_MatchesResxForKnownError()
    {
        MessageCodeCatalog.TryGet(MessageCode.CheckerTypeMismatch, out var entry).Should().BeTrue();
        entry.Severity.Should().Be(DiagnosticSeverity.Error);
        entry.ShortMessage.Should().Be(Message.LocalizeRaw("ERROR_TYHP4008"));
        entry.ShortMessage.Should().NotBe("ERROR_TYHP4008");
    }

    [Fact]
    public void Catalog_ReportsEverySeverityACodeCarriesTextFor()
    {
        // LintNoSourceFiles is a warning for explicit paths and info for an empty project, so the
        // catalog must not collapse it to a single severity.
        MessageCodeCatalog.TryGet(MessageCode.LintNoSourceFiles, out var entry).Should().BeTrue();
        entry.Variants.Select(v => v.Severity).Should().Equal(
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Info);

        MessageCodeCatalog.TryGet(MessageCode.CheckerTypeMismatch, out var single).Should().BeTrue();
        single.Variants.Should().ContainSingle();
        single.ShortMessage.Should().Be(single.Variants[0].ShortMessage);
    }

    [Fact]
    public void FormatExplanationLines_LabelsEachSeverityWhenWordingDiffers()
    {
        // Synthetic multi-severity entry (real multi-severity codes share wording today).
        var entry = new MessageCodeEntry(
            MessageCode.CheckerVariablePossiblyNull,
            nameof(MessageCode.CheckerVariablePossiblyNull),
            (int)MessageCode.CheckerVariablePossiblyNull,
            [
                new MessageCodeVariant(
                    DiagnosticSeverity.Error,
                    "ERROR_TYHP4015",
                    "Variable `${0}` is possibly null here but is used where a non-null value is required"),
                new MessageCodeVariant(
                    DiagnosticSeverity.Warning,
                    "WARNING_TYHP4015",
                    "Variable `${0}` is possibly null"),
            ],
            MessageCodeCategory.Checker,
            MessageCodeCatalog.CategoryResourceKey(MessageCodeCategory.Checker));

        var lines = ExplainAction.FormatExplanationLines(entry);

        foreach (var variant in entry.Variants)
        {
            lines.Should().Contain(l => l.Contains(variant.ShortMessage, StringComparison.Ordinal));
        }

        lines.Should().Contain(l => l.Contains(Message.Localize("warning"), StringComparison.Ordinal));
    }

    [Fact]
    public void FormatExplanationLines_UsesAuthoredLongFormWhenPresent()
    {
        MessageCodeCatalog.TryGet(MessageCode.CheckerTypeMismatch, out var entry).Should().BeTrue();
        MessageCodeCatalog.TryGetAuthoredLongForm(entry, out var authored).Should().BeTrue(
            "TYHP4008 has an EXPLAIN_TYHP4008 resource");

        var lines = ExplainAction.FormatExplanationLines(entry);
        var firstSentence = authored.Split('.', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        lines.Should().Contain(l => l.Contains(firstSentence, StringComparison.Ordinal));
        lines.Should().NotContain(l => l.Contains("will be expanded over time", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatExplanationLines_IncludesCodeMessageAndBodyHeader()
    {
        MessageCodeCatalog.TryGet(MessageCode.CheckerTypeMismatch, out var entry).Should().BeTrue();
        var lines = ExplainAction.FormatExplanationLines(entry);

        lines.Should().Contain(l => l.Contains("TYHP4008", StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains(entry.ShortMessage, StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains("Explanation:", StringComparison.Ordinal)
            || l == Message.Localize("CLI_ExplainBodyHeader"));
    }

    [Theory]
    [InlineData(new[] { "--explain", "TYHP4008" }, new[] { "explain", "--code=TYHP4008" })]
    [InlineData(new[] { "--explain=4008" }, new[] { "explain", "--code=4008" })]
    [InlineData(new[] { "--explain", "3003", "--quiet=true" }, new[] { "explain", "--code=3003", "--quiet=true" })]
    [InlineData(new[] { "lint", "--explain", "TYHP4008" }, new[] { "explain", "--code=TYHP4008" })]
    [InlineData(new[] { "build" }, new[] { "build" })]
    public void RewriteExplainAlias_EquivalenceTable(string[] input, string[] expected)
    {
        ActionConfigProvider.RewriteExplainAlias(input).Should().Equal(expected);
    }

    [Fact]
    public void RewriteExplainThenHelp_YieldsHelpAboutExplain()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(
            ["--explain", "TYHP4008", "--help"]);
        var explained = ActionConfigProvider.RewriteExplainAlias(expanded);
        var helped = ActionConfigProvider.RewriteHelpAlias(explained);

        // --code is retained as a harmless passthrough; help only uses --subject.
        helped.Should().Equal("help", "--subject=explain", "--code=TYHP4008");
    }
}
