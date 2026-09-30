using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class CodeQualityMatchArmTests
{
    [Fact]
    public void Check_IfTrue_StillReportsAlwaysTrueCondition()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                if (true) {
                    return 1;
                }
                return 0;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
    }

    [Fact]
    public void Check_MatchTrue_DoesNotWarnOnSubject()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array $members): mixed {
                return match (true) {
                    \count($members) === 0 => 0,
                    \count($members) === 1 => 1,
                    default => 2,
                };
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnreachableArm);
    }

    [Fact]
    public void Check_IfLiteralIdentity_ReportsAlwaysTrueCondition()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                if (1 === 1) {
                    return 1;
                }
                return 0;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
    }

    [Fact]
    public void Check_MatchTrue_AlwaysMatchingArm_WarnsWhenLaterNonDefaultArmsExist()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): mixed {
                return match (true) {
                    1 === 1 => 1,
                    2 === 2 => 2,
                    default => 3,
                };
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableArm);
    }

    [Fact]
    public void Check_MatchTrue_TrueArm_WarnsWhenLaterNonDefaultArmsExist()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): mixed {
                mixed $result = match (true) {
                    true => 1,
                    false => 2,
                    default => 3,
                };
                return $result;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableArm);
    }

    [Fact]
    public void Check_MatchTrue_AlwaysMatchingArm_DoesNotWarnWhenOnlyDefaultFollows()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $someString): string {
                return match (true) {
                    \strlen($someString) > 5 => "ghi",
                    true => "def",
                    default => "jkl",
                };
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnreachableArm);
    }

    [Fact]
    public void Check_ValueMatch_AlwaysMatchingArm_WarnsWhenLaterNonDefaultArmsExist()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): string {
                $otherString = "asdf";
                return match ("asdf") {
                    "xxxx" => "abc",
                    $otherString => "def",
                    "blah" => "ghi",
                    default => "jkl",
                };
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerConditionAlwaysTrueFalse);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnreachableArm);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
