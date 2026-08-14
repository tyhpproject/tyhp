using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class CompilationResultTests
{
    [Fact]
    public void Success_IsTrueWhenNoErrors()
    {
        var result = new CompilationResult();
        result.Success.Should().BeTrue();
        result.GetExitCode().Should().Be(ExitCode.Success);
    }

    [Fact]
    public void Success_IsFalseWhenErrorsExist()
    {
        var result = new CompilationResult();
        result.Diagnostics.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        result.Success.Should().BeFalse();
        result.GetExitCode().Should().Be(ExitCode.CompileError);
    }

    [Fact]
    public void GetExitCode_ReturnsCompileWarningWhenOnlyWarnings()
    {
        var result = new CompilationResult();
        result.Diagnostics.AddWarning(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        result.GetExitCode().Should().Be(ExitCode.CompileWarning);
    }

    [Fact]
    public void GetExitCode_ReturnsCompileErrorWhenOnlyWarningsAndStrictMode()
    {
        var result = new CompilationResult();
        result.Diagnostics.AddWarning(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        result.GetExitCode(strictMode: true).Should().Be(ExitCode.CompileError);
    }

    [Fact]
    public void GetExitCode_StrictModeDoesNotChangeSuccessOrErrorPaths()
    {
        var clean = new CompilationResult();
        clean.GetExitCode(strictMode: true).Should().Be(ExitCode.Success);

        var errors = new CompilationResult();
        errors.Diagnostics.AddError(MessageCode.ParserUnknownError, "a.tyhp", 1, 0);
        errors.GetExitCode(strictMode: true).Should().Be(ExitCode.CompileError);
    }

    [Fact]
    public void GetExitCode_ReturnsGenericErrorWhenCancelled()
    {
        var result = new CompilationResult { WasCancelled = true };
        result.Diagnostics.AddError(MessageCode.LintCancelled, "", 0, 0);
        result.GetExitCode().Should().Be(ExitCode.GenericError);
        result.GetExitCode(strictMode: true).Should().Be(ExitCode.GenericError);
    }

    [Fact]
    public void ParsedFilesAndGlobalScope_CanBeAssigned()
    {
        var result = new CompilationResult
        {
            ParseDuration = TimeSpan.FromMilliseconds(10),
            BindDuration = TimeSpan.FromMilliseconds(5),
            ParseErrorCount = 1,
            BindErrorCount = 2,
            CheckErrorCount = 3,
        };

        result.ParseDuration.TotalMilliseconds.Should().Be(10);
        result.BindDuration.TotalMilliseconds.Should().Be(5);
        result.ParseErrorCount.Should().Be(1);
        result.BindErrorCount.Should().Be(2);
        result.CheckErrorCount.Should().Be(3);
    }
}
