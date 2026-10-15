using System.Linq;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// An array literal that contains <c>null</c> entries is assignable to an <c>array&lt;K, ?V&gt;</c>
/// target, in declarations, returns, and arguments.
/// </summary>
[Trait("Category", "Checker")]
public class NullableValueArrayLiteralTests
{
    [Theory]
    [InlineData("array<string, ?int> $m = ['k' => null, 'j' => 1];")]
    [InlineData("array<int, ?int> $m = [null, 1];")]
    [InlineData("array<string, ?string> $m = ['k' => null, 'j' => 'x'];")]
    [InlineData("array<string, ?int> $m = ['k' => null];")]
    [InlineData("array<int, ?int> $m = [null];")]
    [InlineData("array<string, int|null> $m = ['k' => null, 'j' => 1];")]
    public void Declaration_ArrayLiteralWithNull_IsAssignable(string declaration)
    {
        var errors = Errors($$"""
            <?tyhp
            function f(): void {
                {{declaration}}
                echo \count($m);
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Return_ArrayLiteralWithNull_IsAssignable()
    {
        var errors = Errors("""
            <?tyhp
            function f(): array<string, ?string> {
                return ['k' => null, 'j' => 'x'];
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Argument_ArrayLiteralWithNull_IsAssignable()
    {
        var errors = Errors("""
            <?tyhp
            function takes(array<int, ?int> $values): int { return \count($values); }
            function f(): int {
                return takes([null, 1]);
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Declaration_NullInNonNullableValueArray_StillReportsMismatch()
    {
        var errors = Errors("""
            <?tyhp
            function f(): void {
                array<string, int> $m = ['k' => null, 'j' => 1];
                echo \count($m);
            }
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Declaration_WrongNonNullElement_StillReportsMismatch()
    {
        var errors = Errors("""
            <?tyhp
            function f(): void {
                array<string, ?int> $m = ['k' => null, 'j' => 'x'];
                echo \count($m);
            }
            """);

        errors.Should().NotBeEmpty();
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
