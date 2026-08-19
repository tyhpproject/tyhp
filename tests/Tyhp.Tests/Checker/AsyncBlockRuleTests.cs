using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class AsyncBlockRuleTests
{
    [Fact]
    public void Check_AwaitInsideAsyncBlock_DoesNotReportOutsideAsync()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            function wrap(int $id): \Tyhp\Promise<string> {
                return async {
                    return await fetchRawAsync($id);
                };
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitOutsideAsync_StillReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            function wrap(int $id): string {
                return await fetchRawAsync($id);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AsyncBlockReturnAssignableToPromise()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function wrap(): \Tyhp\Promise<int> {
                return async {
                    return 1;
                };
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void Check_AsyncMethodCall_IsPromiseWithoutAwait()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            function wrap(int $id): \Tyhp\Promise<string> {
                return fetchRawAsync($id);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void Check_AsyncGenericMethod_ArrayReturnTypeResolvesT()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                async public static function all<T>(array<T> $items): array<T> {
                    return $items;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_AsyncClosureIsNotAnAsyncBlock()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function wrap(): \Tyhp\Promise<int> {
                return async function (): int {
                    return 1;
                };
            }
            """);

        diagnostics.Errors.Should().NotBeEmpty();
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
            var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
            bindErrors.Should().BeEmpty(
                $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");
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
