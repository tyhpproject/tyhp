using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// CHECKER_GAPS P1 #12 / Story 28: omitted type arguments use declared defaults.
/// </summary>
[Trait("Category", "Checker")]
public class GenericDefaultTypeArgumentTests
{
    [Fact]
    public void Check_BareGenericWithDefault_AppliesDefault_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box<T = string> {
                public T $value;
            }
            function take(Box $b): Box<string> { return $b; }
            function make(): Box { return new Box(); }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch
                || d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerIncompatibleReturnType,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_PartialTypeArgs_FillsTrailingDefault()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyMap<TKey, TValue = mixed> {
                public TKey $k;
                public TValue $v;
            }
            function take(MyMap<int> $m): MyMap<int, mixed> { return $m; }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch
                || d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerIncompatibleReturnType,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_DefaultReferencingEarlierParam_Substitutes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Pair<T, U = T> {
                public T $a;
                public U $b;
            }
            function take(Pair<int> $p): Pair<int, int> { return $p; }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch
                || d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerIncompatibleReturnType,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NonDefaultAfterDefault_Reports4311()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bad<T = int, U> {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerGenericNonDefaultAfterDefault);
    }

    [Fact]
    public void Check_DefaultDoesNotSatisfyConstraint_Reports4310()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bad<T extends \Countable = string> {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerGenericDefaultDoesNotSatisfyConstraint);
    }

    [Fact]
    public void Check_CircularDefault_Reports4312()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bad<T = T> {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerGenericDefaultCircularReference);
    }

    [Fact]
    public void Check_MissingRequiredBeforeDefault_StillReportsArity()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyMap<TKey, TValue = mixed> {}
            function bad(MyMap $m): void {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch);
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
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
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
