using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DiagnosticTests
{
    [Fact]
    public void ErrorFactory_SetsSeverityAndProperties()
    {
        var diagnostic = Diagnostic.Error(MessageCode.ParserUnexpectedError, "test.tyhp", 3, 5, new object[] { ";" });
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.Code.Should().Be(MessageCode.ParserUnexpectedError);
        diagnostic.FileName.Should().Be("test.tyhp");
        diagnostic.Line.Should().Be(3);
        diagnostic.Column.Should().Be(5);
        diagnostic.FormatParams.Should().Contain(";");
    }

    [Fact]
    public void WarningFactory_SetsWarningSeverity()
    {
        Diagnostic.Warning(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, Array.Empty<object>())
            .Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void InfoFactory_SetsInfoSeverity()
    {
        Diagnostic.Info(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, Array.Empty<object>())
            .Severity.Should().Be(DiagnosticSeverity.Info);
    }

    [Fact]
    public void RecordEquality_MatchesForSameValues()
    {
        var left = Diagnostic.Error(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, Array.Empty<object>());
        var right = Diagnostic.Error(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, Array.Empty<object>());
        left.Should().Be(right);
    }

    [Fact]
    public void WithHelpAndSuggestion_AttachOptionalFields()
    {
        var diagnostic = Diagnostic.Warning(
                MessageCode.ParserUnknownError,
                "a.tyhp",
                2,
                3,
                Array.Empty<object>())
            .WithHelp("try again")
            .WithNote("see docs")
            .WithSuggestion(DiagnosticSuggestion.Create(
                new DiagnosticSpan("a.tyhp", 2, 3, 2, 6),
                "fixed"));

        diagnostic.Help.Should().Be("try again");
        diagnostic.Note.Should().Be("see docs");
        diagnostic.Suggestion.Should().NotBeNull();
        diagnostic.Suggestion!.Value.Replacement.Should().Be("fixed");
        diagnostic.GetPrimarySpan().Column.Should().Be(3);
    }
}
