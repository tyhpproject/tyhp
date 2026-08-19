using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Exceptions;
using Tyhp.LanguageServer.Handlers;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
using TyhpDiagnostic = Tyhp.Domain.Diagnostics.Diagnostic;
using TyhpSeverity = Tyhp.Domain.Diagnostics.DiagnosticSeverity;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DiagnosticsPublisherTests
{
    [Fact]
    public void ToLspDiagnostic_MapsSeverityCodeMessageAndSource()
    {
        var diagnostic = TyhpDiagnostic.Error(
            MessageCode.ParserUnknownError,
            "src/app.tyhp",
            4,
            2,
            ["oops"],
            endLine: 4,
            endColumn: 8);

        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(diagnostic);

        lsp.Source.Should().Be(DiagnosticsPublisher.DiagnosticSource);
        lsp.Severity.Should().Be(LspDiagnosticSeverity.Error);
        lsp.Message.Should().Be(diagnostic.Message);
        lsp.Range.Start.Line.Should().Be(3);
        lsp.Range.Start.Character.Should().Be(2);
        lsp.Range.End.Line.Should().Be(3);
        lsp.Range.End.Character.Should().Be(8);
        AssertCodeEquals(lsp, (int)MessageCode.ParserUnknownError);
    }

    [Fact]
    public void ToLspDiagnostic_WarningInfoHint_MapToLspSeverities()
    {
        DiagnosticsPublisher.ToLspDiagnostic(
                TyhpDiagnostic.Warning(MessageCode.CheckerUnusedImport, "a.tyhp", 1, 0, ["U"]))
            .Severity.Should().Be(LspDiagnosticSeverity.Warning);
        DiagnosticsPublisher.ToLspDiagnostic(
                TyhpDiagnostic.Info(MessageCode.CheckerEvalUsage, "a.tyhp", 1, 0, ["e"]))
            .Severity.Should().Be(LspDiagnosticSeverity.Information);
        DiagnosticsPublisher.ToLspDiagnostic(
                new TyhpDiagnostic(TyhpSeverity.Hint, MessageCode.CheckerEvalUsage, "a.tyhp", 1, 0, ["h"]))
            .Severity.Should().Be(LspDiagnosticSeverity.Hint);
    }

    [Fact]
    public void ToLspDiagnostic_UnusedImport_HasUnnecessaryTag()
    {
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(
            TyhpDiagnostic.Warning(MessageCode.CheckerUnusedImport, "a.tyhp", 2, 0, ["Foo"]));
        lsp.Tags.Should().NotBeNull();
        lsp.Tags.Should().Contain(DiagnosticTag.Unnecessary);
    }

    [Fact]
    public void ToLspDiagnostic_DeprecatedUsage_HasDeprecatedTag()
    {
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(
            TyhpDiagnostic.Warning(MessageCode.CheckerDeprecatedUsage, "a.tyhp", 2, 0, ["old"]));
        lsp.Tags.Should().NotBeNull();
        lsp.Tags.Should().Contain(DiagnosticTag.Deprecated);
    }

    private static void AssertCodeEquals(LspDiagnostic diagnostic, int expected)
    {
        object? code = diagnostic.Code;
        if (code is int intCode)
        {
            intCode.Should().Be(expected);
            return;
        }

        if (code is SumType<int, string> sum)
        {
            if (sum.Value is int sumInt)
            {
                sumInt.Should().Be(expected);
                return;
            }
        }

        code.Should().NotBeNull("diagnostic code should be populated");
        Convert.ToInt32(code is SumType<int, string> inner ? inner.Value : code).Should().Be(expected);
    }
}
