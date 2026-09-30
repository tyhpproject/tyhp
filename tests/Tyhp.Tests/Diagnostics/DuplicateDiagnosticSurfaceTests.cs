using System.Text.Json;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.LanguageServer.Handlers;
using Tyhp.Tests.Binder;
using Tyhp.Tests.TestHelpers;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DuplicateDiagnosticSurfaceTests
{
    private static string DeclaredHere => Message.Localize("CLI_DiagnosticLabelDeclaredHere");

    private const string DuplicateUserSource = """
        <?tyhp
        class User {}
        class User {}
        """;

    [Fact]
    public void CliRichOutput_DuplicateClass_ShowsDeclaredHereOnFirstDeclaration()
    {
        var error = BindDuplicateUser();
        error.Line.Should().BeGreaterThan(error.Labels[0].Span.Line);

        var sourceLines = DuplicateUserSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var snippet = RichDiagnosticRenderer.BuildSnippetLines(
            error,
            (_, line) => line >= 1 && line <= sourceLines.Length ? sourceLines[line - 1] : null);

        snippet.Should().Contain(l => l.Contains("class User", StringComparison.Ordinal));
        snippet.Should().Contain(l =>
            l.Contains(DeclaredHere, StringComparison.Ordinal)
            && l.Contains('-', StringComparison.Ordinal));
        snippet.Should().Contain(l => l.Contains('^', StringComparison.Ordinal));
    }

    [Fact]
    public void JsonFormat_DuplicateClass_LabelsZeroHasOriginalFileAndLine()
    {
        var error = BindDuplicateUser();

        using var writer = new StringWriter();
        var formatter = new JsonDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(error);
        formatter.SetContext(new CompilationResult { SourceFileCount = 1 });
        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var diag = doc.RootElement.GetProperty("diagnostics")[0];
        diag.GetProperty("code").GetString().Should().Be("TYHP3002");
        diag.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32()
            .Should().Be(error.Line - 1);

        var label = diag.GetProperty("labels")[0];
        label.GetProperty("message").GetString().Should().Be(DeclaredHere);
        label.GetProperty("file").GetString().Should().Be(error.Labels[0].Span.FileName);
        label.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32()
            .Should().Be(error.Labels[0].Span.Line - 1);
        label.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32()
            .Should().BeLessThan(diag.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public void SarifFormat_DuplicateClass_RelatedLocationsIncludeOriginalSpan()
    {
        var error = BindDuplicateUser();

        using var writer = new StringWriter();
        var formatter = new SarifDiagnosticFormatter(writer);
        var bag = new DiagnosticBag();
        bag.Add(error);
        bag.DisplayAll(formatter);

        using var doc = JsonDocument.Parse(writer.ToString());
        var result = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        result.GetProperty("ruleId").GetString().Should().Be("TYHP3002");

        var related = result.GetProperty("relatedLocations")[0];
        related.GetProperty("message").GetProperty("text").GetString().Should().Be(DeclaredHere);
        related.GetProperty("physicalLocation").GetProperty("region").GetProperty("startLine").GetInt32()
            .Should().Be(error.Labels[0].Span.Line);
        related.GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString()
            .Should().NotBeNullOrWhiteSpace();
        related.GetProperty("physicalLocation").GetProperty("region").GetProperty("startLine").GetInt32()
            .Should().BeLessThan(
                result.GetProperty("locations")[0]
                    .GetProperty("physicalLocation")
                    .GetProperty("region")
                    .GetProperty("startLine")
                    .GetInt32());
    }

    [Fact]
    public void Lsp_DuplicateClass3002_RelatedInformationIsNonEmpty()
    {
        var error = BindDuplicateUser();
        Uri documentUri = Path.IsPathRooted(error.FileName)
            ? new Uri(Path.GetFullPath(error.FileName))
            : new Uri("file:///tmp/" + Path.GetFileName(error.FileName));

        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(error, documentUri);
        var mapped = lsp.Should().BeOfType<TyhpLspDiagnostic>().Subject;
        mapped.RelatedInformation.Should().NotBeNull();
        mapped.RelatedInformation.Should().NotBeEmpty();
        mapped.RelatedInformation![0].Message.Should().Be(DeclaredHere);
        mapped.RelatedInformation[0].Location.Uri.Should().NotBeNull();
        mapped.RelatedInformation[0].Location.Range.Start.Line.Should().BeLessThan(lsp.Range.Start.Line);
    }

    [Fact]
    public void Lsp_DuplicateFunctionAcrossFiles_RelatedInformationUsesFirstFileUri()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var firstPath = Path.Combine(tempDir, "first.tyhp");
        var secondPath = Path.Combine(tempDir, "second.tyhp");
        File.WriteAllText(firstPath, """
            <?tyhp
            function demo(): void {}
            """);
        File.WriteAllText(secondPath, """
            <?tyhp
            function demo(): void {}
            """);

        try
        {
            var (_, diagnostics) = BinderTestHelper.BindFiles(firstPath, secondPath);
            var error = diagnostics.Errors
                .Should()
                .ContainSingle(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
                .Subject;

            // The client has "second.tyhp" open — its document URI must NOT be reused
            // for a label that actually belongs to "first.tyhp".
            var documentUri = new Uri(Path.GetFullPath(secondPath));
            LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(error, documentUri);
            var mapped = lsp.Should().BeOfType<TyhpLspDiagnostic>().Subject;
            mapped.RelatedInformation.Should().NotBeNull();
            mapped.RelatedInformation.Should().ContainSingle();

            var related = mapped.RelatedInformation![0];
            related.Location.Uri.Should().NotBe(documentUri);
            related.Location.Uri.IsFile.Should().BeTrue();
            Path.GetFileName(related.Location.Uri.LocalPath).Should().Be("first.tyhp");
            related.Location.Range.Start.Line.Should().Be(error.Labels[0].Span.Line - 1);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void OverlayLastWinsReplace_StillHasNo8002()
    {
        using var builder = IsolatedCompilation.CreateOverlayProject(
            includeTyhpdef: """
                <?tyhpdef
                function overlay_target(): int;
                """,
            overlayTyhpdef: """
                <?tyhpdef
                function overlay_target(): string;
                """);

        var result = IsolatedCompilation.BindProject(builder);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
    }

    private static IDiagnostic BindDuplicateUser()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent(DuplicateUserSource, "user.tyhp");
        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
            .Subject;
        error.Labels.Should().ContainSingle();
        error.Labels[0].Message.Should().Be(DeclaredHere);
        return error;
    }
}
