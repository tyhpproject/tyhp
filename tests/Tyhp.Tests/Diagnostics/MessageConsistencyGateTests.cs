using System.Xml.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class MessageConsistencyGateTests
{
    /// <summary>A code that is guaranteed not to exist in <see cref="MessageCode"/>.</summary>
    private const int UnallocatedCode = 9999;

    /// <summary>An allocated code the mutation tests rewrite (<c>ParserUnknownError</c>).</summary>
    private const int SampleCode = 1001;

    private static string NeutralResxPath
        => Path.Combine(TestFileManager.GetRepoRoot(), "Resources", "CLI.TyhpHostedService.resx");

    private static string EnUsResxPath
        => Path.Combine(
            TestFileManager.GetRepoRoot(),
            "Resources",
            "CLI.TyhpHostedService.en-US.resx");

    [Fact]
    public void ConsistencyGate_IsGreen()
    {
        var violations = MessageConsistencyGate.CollectViolations(NeutralResxPath, EnUsResxPath);

        violations.Should().BeEmpty(
            "Story 14 Phase 5 message consistency gate failed:\n- "
            + string.Join("\n- ", violations));
    }

    [Fact]
    public void ConsistencyGate_AllowlistsOnlyKnownMultiSeverityCodes()
    {
        MessageConsistencyGate.MultiSeverityAllowlist.Should().BeEquivalentTo(
        [
            MessageCode.BinderUnknownError,
            MessageCode.LintNoSourceFiles,
        ]);

        foreach (var code in MessageConsistencyGate.MultiSeverityAllowlist)
        {
            MessageCodeCatalog.TryGet(code, out var entry).Should().BeTrue();
            entry.Variants.Count.Should().BeGreaterThan(
                1,
                because: $"{MessageCodeCatalog.FormatCode(code)} is allowlisted as multi-severity "
                    + "and must carry more than one .resx severity entry");
        }
    }

    [Fact]
    public void ConsistencyGate_DetectsMissingResxEntry()
    {
        var violations = RunAgainstMutatedCatalog(doc => RemoveEntry(doc, $"ERROR_TYHP{SampleCode}"));

        violations.Should().ContainSingle()
            .Which.Should().Contain($"MessageCode.{MessageCode.ParserUnknownError}")
            .And.Contain("has no");
    }

    [Fact]
    public void ConsistencyGate_DetectsResxEntryForUnallocatedCode()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{UnallocatedCode}", "Orphaned catalog entry"));

        violations.Should().ContainSingle()
            .Which.Should().Contain($"ERROR_TYHP{UnallocatedCode}")
            .And.Contain("not defined in MessageCode.cs");
    }

    [Fact]
    public void ConsistencyGate_DetectsTrailingPeriod()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "Unknown parser error: {0}."));

        violations.Should().ContainSingle().Which.Should().Contain("ends with a period");
    }

    [Fact]
    public void ConsistencyGate_AllowsTrailingEllipsis()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "Unknown parser error: {0}..."));

        violations.Should().BeEmpty();
    }

    [Fact]
    public void ConsistencyGate_DetectsUnbacktickedInterpolatedIdentifier()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "Symbol {0} is not found"));

        violations.Should().ContainSingle().Which.Should().Contain("without backticks");
    }

    [Fact]
    public void ConsistencyGate_DetectsQuotedInterpolatedIdentifier()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "Symbol '{0}' is not found"));

        violations.Should().Contain(v => v.Contains("wraps an interpolated placeholder"));
    }

    [Fact]
    public void ConsistencyGate_AcceptsBacktickedInterpolatedIdentifier()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "Symbol `{0}` is not found"));

        violations.Should().BeEmpty();
    }

    [Fact]
    public void ConsistencyGate_DetectsEmptyShortMessage()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "   "));

        violations.Should().ContainSingle().Which.Should().Contain("empty short message");
    }

    [Fact]
    public void ConsistencyGate_DetectsUnallowlistedMultiSeverityCode()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"WARNING_TYHP{SampleCode}", "Unknown parser error: {0}"));

        violations.Should().ContainSingle()
            .Which.Should().Contain("carries multiple severity")
            .And.Contain($"TYHP{SampleCode}");
    }

    [Fact]
    public void ConsistencyGate_DetectsEmptyExplainBody()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"EXPLAIN_TYHP{SampleCode}", string.Empty));

        violations.Should().ContainSingle().Which.Should().Contain("is present but empty");
    }

    [Fact]
    public void ConsistencyGate_DetectsExplainBodyForUnallocatedCode()
    {
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(doc, $"EXPLAIN_TYHP{UnallocatedCode}", "Long-form prose for a dead code."));

        violations.Should().ContainSingle()
            .Which.Should().Contain($"EXPLAIN_TYHP{UnallocatedCode}")
            .And.Contain("not defined in MessageCode.cs");
    }

    [Fact]
    public void ConsistencyGate_DetectsExplainBodyCopiedFromUnrelatedCode()
    {
        // Shifted leftover: internal-member prose under a composite-type / utility-type code.
        const int topicSampleCode = 4050;
        var violations = RunAgainstMutatedCatalog(
            doc => SetEntry(
                doc,
                $"EXPLAIN_TYHP{topicSampleCode}",
                "Code in one project references an `internal` member from a different project."));

        violations.Should().ContainSingle()
            .Which.Should().Contain($"EXPLAIN_TYHP{topicSampleCode}")
            .And.Contain("does not mention its diagnostic topic")
            .And.Contain("CheckerUtilityTypeInvalidKey");
    }

    [Fact]
    public void ConsistencyGate_DetectsCultureDrift()
    {
        var violations = RunAgainstMutatedCatalog(
            mutateNeutral: doc => SetEntry(doc, $"ERROR_TYHP{SampleCode}", "Unknown parser error `{0}`"),
            mutateEnUs: null);

        violations.Should().ContainSingle().Which.Should().Contain("text differs between");
    }

    [Fact]
    public void ConsistencyGate_DetectsKeyPresentInOnlyOneCulture()
    {
        var violations = RunAgainstMutatedCatalog(
            mutateNeutral: null,
            mutateEnUs: doc => RemoveEntry(doc, $"ERROR_TYHP{SampleCode}"));

        violations.Should().ContainSingle()
            .Which.Should().Contain($"ERROR_TYHP{SampleCode}")
            .And.Contain("but not in en-US");
    }

    [Fact]
    public void ConsistencyGate_ReportsMissingResourceFiles()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"tyhp-missing-{Guid.NewGuid():N}.resx");

        MessageConsistencyGate.CollectViolations(missing, EnUsResxPath)
            .Should().ContainSingle().Which.Should().Contain("Missing culture-neutral resource file");

        MessageConsistencyGate.CollectViolations(NeutralResxPath, missing)
            .Should().ContainSingle().Which.Should().Contain("Missing en-US resource file");
    }

    /// <summary>
    /// Copies the real catalog to a temp directory, applies <paramref name="mutate"/> to both
    /// culture files, and runs the gate against the copy. Mutating both keeps the culture-sync
    /// check quiet so the assertion sees only the violation the test induced.
    /// </summary>
    private static IReadOnlyList<string> RunAgainstMutatedCatalog(Action<XDocument> mutate)
        => RunAgainstMutatedCatalog(mutate, mutate);

    private static IReadOnlyList<string> RunAgainstMutatedCatalog(
        Action<XDocument>? mutateNeutral,
        Action<XDocument>? mutateEnUs)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tyhp-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var neutral = Path.Combine(directory, "CLI.TyhpHostedService.resx");
            var enUs = Path.Combine(directory, "CLI.TyhpHostedService.en-US.resx");
            WriteMutated(NeutralResxPath, neutral, mutateNeutral);
            WriteMutated(EnUsResxPath, enUs, mutateEnUs);

            return MessageConsistencyGate.CollectViolations(neutral, enUs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteMutated(string sourcePath, string targetPath, Action<XDocument>? mutate)
    {
        var document = XDocument.Load(sourcePath);
        mutate?.Invoke(document);
        document.Save(targetPath);
    }

    private static void SetEntry(XDocument document, string key, string value)
    {
        var existing = FindEntry(document, key);
        if (existing != null)
        {
            existing.SetElementValue("value", value);
            return;
        }

        document.Root!.Add(new XElement(
            "data",
            new XAttribute("name", key),
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            new XElement("value", value)));
    }

    private static void RemoveEntry(XDocument document, string key)
        => FindEntry(document, key)?.Remove();

    private static XElement? FindEntry(XDocument document, string key)
        => document.Root!
            .Elements("data")
            .FirstOrDefault(d => string.Equals((string?)d.Attribute("name"), key, StringComparison.Ordinal));
}
