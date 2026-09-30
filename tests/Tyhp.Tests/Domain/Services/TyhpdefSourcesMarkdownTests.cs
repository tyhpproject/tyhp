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
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
