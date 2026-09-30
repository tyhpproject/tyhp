using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class CodeQualityRedundantCastTests
{
    [Fact]
    public void Check_IntToIntCast_StillReportsRedundantCast()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(int $n): int {
                return (int)$n;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerRedundantCast);
    }

    [Fact]
    public void Check_MixedOperandCast_DoesNotReportRedundantCast()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $n): float {
                return (float)$n;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerRedundantCast);
    }

    [Fact]
    public void Check_OperatorOverload_StringPropertyNumericCast_DoesNotReportRedundantCast()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Amount {
                public string $value = "0";

                operator +(self $value) {
                    return (float)$value->value;
                }

                operator convert(self $value): int {
                    return (int)$value->value;
                }

                operator convert(self $value): float {
                    return (float)$value->value;
                }
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerRedundantCast);
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
