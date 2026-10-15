using System.Linq;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Symbol-name brands (<c>__ClassName&lt;T&gt;</c>, <c>__InterfaceName&lt;T&gt;</c>) are strings at run
/// time, so <c>echo \get_class($e)</c> and string interpolation accept them like any other string.
/// </summary>
[Trait("Category", "Checker")]
public class EchoSymbolNameBrandTests
{
    [Fact]
    public void Echo_GetClassResult_IsStringable()
    {
        var errors = Errors("""
            <?tyhp
            function f(\Throwable $e): void {
                echo \get_class($e);
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Echo_GetClassResultWithSeparator_IsStringable()
    {
        var errors = Errors("""
            <?tyhp
            function f(\Throwable $e, string $m): void {
                echo \get_class($e), ': ', $m;
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Echo_ClassConstantOfThrowable_IsStringable()
    {
        var errors = Errors("""
            <?tyhp
            function f(\Throwable $e): void {
                echo $e::class;
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Interpolation_GetClassResult_IsStringable()
    {
        var errors = Errors("""
            <?tyhp
            function f(\Throwable $e): string {
                $name = \get_class($e);
                return "{$name}!";
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Echo_NonStringableObject_StillReportsTyhp4120()
    {
        var errors = Errors("""
            <?tyhp
            final class Plain {}
            function f(Plain $p): void {
                echo $p;
            }
            """);

        errors.Should().ContainSingle(e => e.Contains("TYHP4120"));
    }

    private static List<string> Errors(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(path, source);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([path], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics.Errors
                .Select(e => $"TYHP{(int)e.Code}: {e.Message}")
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
