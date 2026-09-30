using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>never</c> is a generic type argument. Callable-shape parameters, function
/// parameters, and properties still reject it.
/// </summary>
[Trait("Category", "Checker")]
public class NeverInGenericPositionTests
{
    [Fact]
    public void UnconstrainedTypeArgument_AllowsNever()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<T> {}
            function reject(): Promise<never> {
                Promise<never> $p = new Promise<never>();
                return $p;
            }
            function bottom(array<never> $items): void {}
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNeverInNonReturnPosition,
            Describe(errors));
        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void CallableShapeParameter_StillRejectsNever()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(callable(never): int $fn): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerNeverInNonReturnPosition);
    }

    [Fact]
    public void Parameter_StillRejectsNever()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(never $value): void {}
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.CheckerNeverInNonReturnPosition);
        errors.Should().Contain(e => e.Code == MessageCode.CheckerNeverNotAllowedHere);
    }

    [Fact]
    public void VoidTypeArgument_StillRequiresOptIn()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<T> {}
            function demo(Box<void> $box): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerVoidInNonReturnPosition);
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
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
