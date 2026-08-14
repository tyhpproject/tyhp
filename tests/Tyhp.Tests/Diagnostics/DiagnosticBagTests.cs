using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DiagnosticBagTests
{
    [Fact]
    public void Add_SingleDiagnostic_AppearsInAll()
    {
        var bag = new DiagnosticBag();
        bag.Add(Diagnostic.Error(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, Array.Empty<object>()));
        bag.All.Should().ContainSingle();
        bag.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void AddErrorWarningInfo_SetSeverityCounts()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddWarning(MessageCode.ParserUnknownError, "a.tyhp", 2, 0);
        bag.AddInfo(MessageCode.ParserUnknownError, "a.tyhp", 3, 0);

        bag.HasErrors.Should().BeTrue();
        bag.HasWarnings.Should().BeTrue();
        bag.ErrorCount.Should().Be(1);
        bag.WarningCount.Should().Be(1);
        bag.InfoCount.Should().Be(1);
    }

    [Fact]
    public void ErrorsAndWarnings_FilterBySeverity()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddWarning(MessageCode.ParserUnknownError, "b.tyhp", 1, 0);

        bag.Errors.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Error);
        bag.Warnings.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void AddRange_MergesDiagnostics()
    {
        var bag = new DiagnosticBag();
        var other = new DiagnosticBag();
        other.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        bag.AddRange(other);
        bag.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void All_OrdersByFileThenLine()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnknownError, "b.tyhp", 2, 0);
        bag.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);

        bag.All.Select(d => d.FileName).Should().ContainInOrder("a.tyhp", "b.tyhp");
    }

    [Fact]
    public void Add_FromMultipleThreads_IsThreadSafe()
    {
        var bag = new DiagnosticBag();
        const int threads = 4;
        const int perThread = 100;

        Parallel.For(0, threads, thread =>
        {
            for (var i = 0; i < perThread; i++)
            {
                // Unique file+line so de-duplication does not collapse concurrent inserts.
                bag.AddError(MessageCode.ParserUnknownError, $"file_{thread}_{i}.tyhp", i + 1, 0);
            }
        });

        bag.ErrorCount.Should().Be(threads * perThread);
    }

    [Fact]
    public void Add_IdenticalDiagnostics_AreDeduplicated()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 3, 9, ";", 9);
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 3, 9, ";", 9);
        bag.AddError(MessageCode.VisitorUnexpectedAlternative, "a.tyhp", 3, 4, "statementRequiringTerminal", "StatementRequiringTerminalContext");

        bag.ErrorCount.Should().Be(2);
        bag.Errors.Should().ContainSingle(d => d.Code == MessageCode.ParserUnexpectedError);
        bag.Errors.Should().ContainSingle(d => d.Code == MessageCode.VisitorUnexpectedAlternative);
    }

    [Fact]
    public void Add_SameCodeDifferentFormatParams_AreKept()
    {
        var bag = new DiagnosticBag();
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 1, 0, ";", 0);
        bag.AddError(MessageCode.ParserUnexpectedError, "a.tyhp", 1, 0, "}", 0);

        bag.ErrorCount.Should().Be(2);
    }
}

