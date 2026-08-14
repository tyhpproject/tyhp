using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Undeclared type names inside <c>callable&lt;…&gt;</c> (and other generic type args) must
/// diagnose — FOUND_BUGS 2026-08-12 callable/<c>TResult</c> audit.
/// </summary>
[Trait("Category", "Checker")]
public class CallableUnresolvedTypeArgumentTests
{
    [Fact]
    public void Callable_UndeclaredTypeArgument_ReportsSymbolNotFound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable<?TResult, int> $cb): int {
                return $cb(null);
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TResult"),
            $"expected TYHP3003 for TResult: {Describe(errors)}");
    }

    [Fact]
    public void Callable_UndeclaredTypeArgument_PointsAtSpelling()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable<?TResult, int> $cb): int {
                return $cb(null);
            }
            """);

        var diagnostic = errors.Should().ContainSingle(
            e => NamesUnresolvedSymbol(e, "TResult")).Subject;

        // `callable<?TResult, int>` — TResult starts after `callable<?`
        diagnostic.Column.Should().BeGreaterThan(0);
        diagnostic.Line.Should().Be(3);
    }

    [Fact]
    public void Callable_InScopeGeneric_DoesNotReport()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<TReturn> {
                public function continueWith<TContinueReturn>(
                    callable<?TReturn, ?\Throwable, TContinueReturn> $continuation
                ): self {
                    return $this;
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.BinderSymbolNotFound,
            $"in-scope TReturn must not diagnose: {Describe(errors)}");
    }

    [Fact]
    public void Callable_PromiseContinueWith_TResult_Reports()
    {
        // Mirror Promise.tyhp continueWith spelling (class generic is TReturn; TResult is wrong).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn extends void|mixed = mixed> {
                public function continueWith<TContinueReturn>(
                    callable<?TResult, ?\Throwable, TContinueReturn> $continuation
                ): self {
                    return new self<TContinueReturn>(function () use ($continuation) {
                        try {
                            mixed $value = null;
                            return $continuation($value, null);
                        } catch (\Throwable $e) {
                            return $continuation(null, $e);
                        }
                    });
                }
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TResult"),
            $"Promise continueWith TResult must diagnose: {Describe(errors)}");
    }

    [Fact]
    public void Array_UndeclaredTypeArgument_ReportsSymbolNotFound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(array<UnknownThing> $arr): void {}
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "UnknownThing"),
            $"array<UnknownThing> must diagnose: {Describe(errors)}");
    }

    [Fact]
    public void UserGeneric_UndeclaredTypeArgument_ReportsSymbolNotFound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<T> {}
            function take(Box<UnknownThing> $b): void {}
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "UnknownThing"),
            $"Box<UnknownThing> must diagnose: {Describe(errors)}");
    }

    [Fact]
    public void TopLevel_UnknownParameterType_StillBinder3020_NotDuplicate3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(UnknownThing $x): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.BinderUnresolvedParameterType);
        errors.Should().NotContain(
            e => e.Code == MessageCode.BinderSymbolNotFound,
            "top-level unresolved params stay binder-only (no duplicate TYHP3003)");
    }

    private static bool NamesUnresolvedSymbol(IDiagnostic diagnostic, string name) =>
        diagnostic.Code == MessageCode.BinderSymbolNotFound
        && diagnostic.FormatParams.Length > 0
        && string.Equals(diagnostic.FormatParams[0]?.ToString(), name, StringComparison.Ordinal);

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

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
