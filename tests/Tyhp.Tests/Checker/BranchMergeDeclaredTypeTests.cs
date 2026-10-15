using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Joining if/else branches keeps an explicitly typed local's declared type: assignments after the
/// join are checked against the declaration, not against the flow-narrowed type both branches saw.
/// </summary>
[Trait("Category", "Checker")]
public class BranchMergeDeclaredTypeTests
{
    private const string Prelude = """
        <?tyhp
        class Res {}
        function nextRes(): Res|false { return false; }
        function nextStr(): string|false { return false; }

        """;

    [Fact]
    public void Check_IfElseInsideNarrowingLoop_ReassignmentUsesDeclaredType()
    {
        var errors = CompileAndCheck(Prelude + """
            function loopBranches(): int {
                int $count = 0;
                ?Res $last = null;
                Res|false $result = nextRes();
                while ($result !== false) {
                    if ($count > 3) {
                        $count++;
                    } else {
                        $last = $result;
                    }
                    $result = nextRes();
                }
                return $last === null ? $count : 0;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"`$result` is declared `Res|false`; the if/else join must not shrink it to `Res`: {Describe(errors)}");
    }

    [Fact]
    public void Check_IfElseInsideNarrowingLoop_ScalarUnion_ReassignmentUsesDeclaredType()
    {
        var errors = CompileAndCheck(Prelude + """
            function loopBranches(): int {
                int $count = 0;
                string|false $line = nextStr();
                while ($line !== false) {
                    if ($count > 3) {
                        $count++;
                    } else {
                        $count += 2;
                    }
                    $line = nextStr();
                }
                return $count;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"`$line` is declared `string|false`: {Describe(errors)}");
    }

    [Fact]
    public void Check_IfElseInsideNarrowingIf_ReassignmentUsesDeclaredType()
    {
        var errors = CompileAndCheck(Prelude + """
            function branches(bool $flag): void {
                Res|false $result = nextRes();
                if ($result !== false) {
                    if ($flag) {
                        $flag = false;
                    } else {
                        $flag = true;
                    }
                    $result = nextRes();
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"`$result` is declared `Res|false`: {Describe(errors)}");
    }

    [Fact]
    public void Check_IfElseJoin_KeepsNarrowingForReads()
    {
        var errors = CompileAndCheck(Prelude + """
            function branches(bool $flag): Res {
                Res|false $result = nextRes();
                if ($result === false) {
                    return new Res();
                }
                if ($flag) {
                    $flag = false;
                } else {
                    $flag = true;
                }
                return $result;
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch || d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"narrowed `Res` survives an unrelated if/else join for reads: {Describe(errors)}");
    }

    [Fact]
    public void Check_IfElseJoin_ReassignmentOutsideDeclaredType_StillReports()
    {
        var errors = CompileAndCheck(Prelude + """
            function branches(bool $flag): void {
                Res|false $result = nextRes();
                while ($result !== false) {
                    if ($flag) {
                        $flag = false;
                    } else {
                        $flag = true;
                    }
                    $result = 'not a result';
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"assignment is still checked against the declared `Res|false`: {Describe(errors)}");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
