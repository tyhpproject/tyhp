using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class TyhpdefSourcesMarkdownTests
{
    [Fact]
    public void WriteManualCredit_SecondExtension_AppendsBullet()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-sources-").FullName;
        try
        {
            Tyhp.Domain.Services.TyhpdefSourcesMarkdown.WriteManualCredit(outputDir, "json");
            Tyhp.Domain.Services.TyhpdefSourcesMarkdown.WriteManualCredit(outputDir, "standard");
            Tyhp.Domain.Services.TyhpdefSourcesMarkdown.WriteManualCredit(outputDir, "json");

            var text = File.ReadAllText(Path.Combine(outputDir, "SOURCES.md"));
            text.Should().Contain("- Extension: `json`");
            text.Should().Contain("- Extension: `standard`");
            text.Should().Contain("PHP Documentation Group");
            text.Split("- Extension: `json`", StringSplitOptions.None).Length.Should().Be(2);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void WriteManualCredit_WritesCcByAttributionToNotice_Once()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-notice-manual-").FullName;
        try
        {
            TyhpdefSourcesMarkdown.WriteManualCredit(outputDir, "json");
            TyhpdefSourcesMarkdown.WriteManualCredit(outputDir, "standard");

            var notice = File.ReadAllText(Path.Combine(outputDir, "NOTICE"));
            notice.Should().Contain("Copyright © The PHP Documentation Group.");
            notice.Should().Contain("https://creativecommons.org/licenses/by/3.0/");
            notice.Split(TyhpdefSourcesMarkdown.ManualNoticeHeading, StringSplitOptions.None).Length.Should().Be(2);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void AppendStubCorpora_ReproducesUpstreamLicenseText()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-notice-stubs-").FullName;
        try
        {
            TyhpdefSourcesMarkdown.AppendStubCorpora(outputDir, "json", StubCorpusCache.AllCorpora);

            var notice = File.ReadAllText(Path.Combine(outputDir, "NOTICE"));
            notice.Should().Contain("Copyright (c) 2016 Vimeo");
            notice.Should().Contain("Copyright (c) 2016 Ondřej Mirtes");
            notice.Should().Contain("Copyright (c) 2025 PHPStan s.r.o.");
            notice.Should().Contain("Copyright (c) 2015 Rasmus Lerdorf");
            notice.Should().Contain("Copyright 2010-2023 JetBrains s.r.o.");
            notice.Should().Contain("The above copyright notice and this permission notice shall be included in all");
            notice.Should().Contain("Licensed under the Apache License, Version 2.0");
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void StubAndManualNotices_AreStableInEitherOrderAndOnRepeat()
    {
        var manualFirst = Directory.CreateTempSubdirectory("tyhpdef-notice-order-a-").FullName;
        var stubsFirst = Directory.CreateTempSubdirectory("tyhpdef-notice-order-b-").FullName;
        try
        {
            TyhpdefSourcesMarkdown.WriteManualCredit(manualFirst, "json");
            TyhpdefSourcesMarkdown.AppendStubCorpora(manualFirst, "json", StubCorpusCache.AllCorpora);
            TyhpdefSourcesMarkdown.WriteManualCredit(manualFirst, "json");

            TyhpdefSourcesMarkdown.AppendStubCorpora(stubsFirst, "json", StubCorpusCache.AllCorpora);
            TyhpdefSourcesMarkdown.WriteManualCredit(stubsFirst, "json");
            TyhpdefSourcesMarkdown.AppendStubCorpora(stubsFirst, "json", StubCorpusCache.AllCorpora);

            var a = File.ReadAllText(Path.Combine(manualFirst, "NOTICE"));
            var b = File.ReadAllText(Path.Combine(stubsFirst, "NOTICE"));
            a.Should().Be(b);
            a.Split(TyhpdefSourcesMarkdown.StubNoticeHeading, StringSplitOptions.None).Length.Should().Be(2);
            a.IndexOf(TyhpdefSourcesMarkdown.ManualNoticeHeading, StringComparison.Ordinal)
                .Should().BeLessThan(a.IndexOf(TyhpdefSourcesMarkdown.StubNoticeHeading, StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(manualFirst);
            TryDelete(stubsFirst);
        }
    }

    [Fact]
    public void AppendStubCorpora_UpgradesLegacyShortNotice_AndKeepsHandWrittenText()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-notice-legacy-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(outputDir, "NOTICE"),
                "Hand-written note.\n\n"
                + "Stub corpora (Layer 2 tyhpdef harvest)\n"
                + "- Psalm stubs: MIT (https://github.com/vimeo/psalm/tree/6.x/stubs)\n");
            File.WriteAllText(
                Path.Combine(outputDir, "SOURCES.md"),
                "# Sources\n\n" + TyhpdefSourcesMarkdown.StubHeading + "\n");

            TyhpdefSourcesMarkdown.AppendStubCorpora(outputDir, "json", StubCorpusCache.AllCorpora);

            var notice = File.ReadAllText(Path.Combine(outputDir, "NOTICE"));
            notice.Should().StartWith("Hand-written note.");
            notice.Should().Contain("Copyright (c) 2016 Vimeo");
            notice.Split(TyhpdefSourcesMarkdown.StubNoticeHeading, StringSplitOptions.None).Length.Should().Be(2);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
