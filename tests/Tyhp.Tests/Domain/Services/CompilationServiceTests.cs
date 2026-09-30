using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

/// <summary>
/// Compilation pipeline orchestration (FOUND_BUGS #48): parse errors in one file
/// must not skip bind/check for sibling files that parsed.
/// </summary>
[Trait("Category", "Compilation")]
public class CompilationServiceTests
{
    [Fact]
    public void ParseFiles_ParseErrorInOneFile_StillReportsTypeErrorInSibling()
    {
        using var project = CompileTwoFiles(
            badFileName: "bad.tyhp",
            badSource: """
                <?tyhp
                function broken(
                """,
            goodFileName: "good.tyhp",
            goodSource: """
                <?tyhp
                function typed(): int {
                    return "nope";
                }
                """);

        project.Result.ParseErrorCount.Should().BeGreaterThan(0);
        project.Result.Diagnostics.Errors.Should().Contain(
            d => IsParsePhaseError(d.Code) && IsForFile(d, "bad.tyhp"),
            $"parse error on the broken file must remain visible: {Describe(project.Result)}");

        project.Result.GlobalScope.Should().NotBeNull(
            "files that parsed must still bind even when a sibling has a parse error");
        project.Result.CheckDuration.Should().BeGreaterThan(
            TimeSpan.Zero,
            "files that parsed must still be checked");

        project.Result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType && IsForFile(d, "good.tyhp"),
            $"type error on the file that parsed must still be reported: {Describe(project.Result)}");
    }

    [Fact]
    public void ParseFiles_AllFilesHaveParseErrors_DoesNotInventEmptyBindError()
    {
        using var project = CompileTwoFiles(
            badFileName: "one.tyhp",
            badSource: """
                <?tyhp
                function broken(
                """,
            goodFileName: "two.tyhp",
            goodSource: """
                <?tyhp
                class Incomplete {
                """);

        project.Result.ParseErrorCount.Should().BeGreaterThan(0);
        project.Result.Diagnostics.Errors.Should().OnlyContain(
            d => IsParsePhaseError(d.Code),
            $"only parse diagnostics are expected when nothing parsed: {Describe(project.Result)}");
        project.Result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.BinderUnknownError,
            "an empty bind list must not add a 'no source files' binder error");
        project.Result.CheckDuration.Should().Be(TimeSpan.Zero);
        project.Result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void ParseFiles_NoParseErrors_StillBindsAndChecks()
    {
        using var project = CompileTwoFiles(
            badFileName: "ok.tyhp",
            badSource: """
                <?tyhp
                function ok(): int {
                    return 1;
                }
                """,
            goodFileName: "typed.tyhp",
            goodSource: """
                <?tyhp
                function typed(): int {
                    return "nope";
                }
                """);

        project.Result.ParseErrorCount.Should().Be(0);
        project.Result.GlobalScope.Should().NotBeNull();
        project.Result.CheckDuration.Should().BeGreaterThan(TimeSpan.Zero);
        project.Result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType && IsForFile(d, "typed.tyhp"),
            Describe(project.Result));
        project.Result.Diagnostics.Errors.Should().NotContain(d => IsParsePhaseError(d.Code));
    }

    private static CompiledProject CompileTwoFiles(
        string badFileName,
        string badSource,
        string goodFileName,
        string goodSource)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var badPath = Path.Combine(tempDir, badFileName);
        var goodPath = Path.Combine(tempDir, goodFileName);
        File.WriteAllText(badPath, badSource);
        File.WriteAllText(goodPath, goodSource);

        using var compilationService = new CompilationService();
        var result = compilationService.ParseFiles(
            [badPath, goodPath],
            IsolatedCompilation.CreateOptions(tempDir));
        return new CompiledProject(tempDir, result);
    }

    private static bool IsParsePhaseError(MessageCode code) =>
        code is MessageCode.ParserUnknownError
            or MessageCode.ParserUnexpectedError
            or MessageCode.ParserCompileAborted
            or MessageCode.VisitorUnknownError
            or MessageCode.VisitorUnexpectedAlternative
            or MessageCode.VisitorMissingRequiredNode;

    private static bool IsForFile(IDiagnostic diagnostic, string fileName) =>
        diagnostic.FileName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)
        || diagnostic.FileName.Contains(fileName, StringComparison.OrdinalIgnoreCase);

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.FileName}:{e.Line} {e.Message}"));

    private sealed class CompiledProject : IDisposable
    {
        public CompiledProject(string tempDir, CompilationResult result)
        {
            this._tempDir = tempDir;
            this.Result = result;
        }

        private readonly string _tempDir;

        public CompilationResult Result { get; }

        public void Dispose()
        {
            try { Directory.Delete(this._tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
