using System.Net;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DiagnosticsReferenceGeneratorTests
{
    private static string ReferencePath
        => Path.Combine(TestFileManager.GetRepoRoot(), "docs", "content", "diagnostics_reference.md");

    /// <summary>
    /// The committed page is checked out with the host's line endings, so compare the text with
    /// CRLF folded away rather than failing the drift check on Windows.
    /// </summary>
    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    [Fact]
    public void DiagnosticsReferenceMarkdown_MatchesGeneratedIndex()
    {
        var generated = DiagnosticsReferenceGenerator.GenerateMarkdown();

        if (Environment.GetEnvironmentVariable("TYHP_UPDATE_DIAGNOSTICS_REFERENCE") == "1")
        {
            File.WriteAllText(ReferencePath, generated);
        }

        File.Exists(ReferencePath).Should().BeTrue(
            "docs/content/diagnostics_reference.md should exist");

        var onDisk = File.ReadAllText(ReferencePath);
        NormalizeNewlines(onDisk).Should().Be(
            NormalizeNewlines(generated),
            "diagnostics_reference.md drifted from MessageCode/.resx. Regenerate with: "
            + "TYHP_UPDATE_DIAGNOSTICS_REFERENCE=1 dotnet test --filter DiagnosticsReferenceGeneratorTests");
    }

    [Fact]
    public void GeneratedIndex_UsesPlatformIndependentNewlines()
    {
        DiagnosticsReferenceGenerator.GenerateMarkdown().Should().NotContain("\r");
    }

    [Fact]
    public void GeneratedIndex_ListsEverySeverityACodeCarriesTextFor()
    {
        var markdown = DiagnosticsReferenceGenerator.GenerateMarkdown();

        var multiSeverity = MessageCodeCatalog.All.Where(e => e.Variants.Count > 1).ToList();
        multiSeverity.Should().NotBeEmpty(
            "the catalog has codes reported at more than one severity (e.g. LintNoSourceFiles)");

        foreach (var entry in multiSeverity)
        {
            var codeId = MessageCodeCatalog.FormatCode(entry.Code);
            var labels = string.Join(", ", entry.Variants.Select(v => v.Severity switch
            {
                DiagnosticSeverity.Warning => "Warning",
                DiagnosticSeverity.Info => "Info",
                DiagnosticSeverity.Hint => "Hint",
                _ => "Error",
            }));

            markdown.Should().Contain(
                $":::member[{codeId} ({labels})]",
                because: $"{codeId} ({entry.Name}) is reported at {labels}");
        }
    }

    [Fact]
    public void GeneratedIndex_InlinesAuthoredLongFormAndLinksTheRest()
    {
        var markdown = DiagnosticsReferenceGenerator.GenerateMarkdown();

        var authored = MessageCodeCatalog.All
            .Where(e => MessageCodeCatalog.TryGetAuthoredLongForm(e, out _))
            .ToList();

        authored.Should().NotBeEmpty(
            "at least one EXPLAIN_TYHP#### body must exist so the documentation slot is exercised");

        foreach (var entry in authored)
        {
            MessageCodeCatalog.TryGetAuthoredLongForm(entry, out var body).Should().BeTrue();
            var firstSentence = body.Split('.', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            markdown.Should().Contain(
                firstSentence,
                because: $"the authored explanation for {MessageCodeCatalog.FormatCode(entry.Code)} "
                    + "must reach the docs index, not just --explain");
        }

        var authoredCodes = authored.Select(e => e.NumericCode).ToHashSet();
        foreach (var entry in MessageCodeCatalog.All.Where(e => !authoredCodes.Contains(e.NumericCode)))
        {
            var codeId = MessageCodeCatalog.FormatCode(entry.Code);
            markdown.Should().Contain(
                $"Run <code>tyhp --explain {codeId}</code> for the long-form explanation.",
                because: $"{codeId} has no authored body yet and must link to --explain");
        }

        // Recovered EXPLAIN bodies keep fenced examples (not collapsed to one line).
        markdown.Should().Contain("```tyhp\n<?tyhp\nint $count = \"hello\";");
    }

    [Fact]
    public void SplitLongFormBlocks_PreservesFencedExamples()
    {
        var blocks = MessageCodeCatalog.SplitLongFormBlocks(
            "Prose one.\n\n```tyhp\n<?tyhp\nint $x = 1;\n```\n\nFix: do the thing.").ToList();

        blocks.Should().HaveCount(3);
        blocks[0].IsCodeFence.Should().BeFalse();
        blocks[0].Text.Should().Be("Prose one.");
        blocks[1].IsCodeFence.Should().BeTrue();
        blocks[1].Text.Should().Be("```tyhp\n<?tyhp\nint $x = 1;\n```");
        blocks[2].Text.Should().Be("Fix: do the thing.");
    }

    [Fact]
    public void GeneratedIndex_ContainsEveryMessageCodeAndLiveShortMessage()
    {
        var markdown = DiagnosticsReferenceGenerator.GenerateMarkdown();

        markdown.Should().Contain(DiagnosticsReferenceGenerator.GeneratedMarker);
        markdown.Should().Contain("tyhp --explain");

        foreach (var entry in MessageCodeCatalog.All)
        {
            var codeId = MessageCodeCatalog.FormatCode(entry.Code);
            markdown.Should().Contain(
                codeId,
                because: $"{codeId} ({entry.Name}) must appear in the generated index");
            markdown.Should().Contain(
                WebUtility.HtmlEncode(entry.ShortMessage),
                because: $"live .resx text for {codeId} must appear (no hardcoded drift)");
        }

        // Spot-check that the old drifted wording is gone.
        markdown.Should().NotContain("Symbol '{0}' not found");
        markdown.Should().NotContain("Multiple visibility modifiers specified");
    }

    [Fact]
    public void Catalog_DoesNotHardcodeCodeList()
    {
        // Adding a new MessageCode must surface in the catalog without editing a table.
        var catalogCount = MessageCodeCatalog.All.Count;
        var enumCount = Enum.GetValues<MessageCode>().Count(c => c != MessageCode.NoError);
        catalogCount.Should().Be(enumCount);
    }
}
