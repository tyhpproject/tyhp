using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Call-site generic inference through a class's implemented/extended generic ancestors
/// (<c>Parallel::runAsync(new MyTask())</c> with <c>MyTask implements ParallelTask&lt;int&gt;</c>), and
/// leading explicit type arguments that leave trailing parameters to inference / defaults.
/// </summary>
[Trait("Category", "Checker")]
public class GenericAncestorInferenceTests
{
    private const string Declarations = """
        interface Task<T> {
            public function run(): T;
        }
        interface Pair<A, B> {
            public function first(): A;
            public function second(): B;
        }
        abstract class BaseTask<R> implements Task<R> {}
        final class CountTask implements Task<int> {
            public function run(): int { return 3; }
        }
        final class DerivedTask extends BaseTask<string> {
            public function run(): string { return 'x'; }
        }
        final class PairTask implements Pair<int, string>, Task<bool> {
            public function first(): int { return 1; }
            public function second(): string { return 's'; }
            public function run(): bool { return true; }
        }
        final class Runner {
            public static function first<T>(Task<T> $task): T { return $task->run(); }
            public static function pairFirst<A, B>(Pair<A, B> $p): A { return $p->first(); }
            public static function many<T>(array<int, Task<T>> $tasks): array<int, T> { return []; }
            public static function each<TItem, TResult, TKey extends int|string = int|string>(
                array<TKey, TItem> $items,
                callable(TItem): TResult $f
            ): array<TKey, TResult> {
                array<TKey, TResult> $out = [];
                foreach ($items as $k => $v) { $out[$k] = $f($v); }
                return $out;
            }
        }
        function eachFree<TItem, TResult, TKey extends int|string = int|string>(
            array<TKey, TItem> $items,
            callable(TItem): TResult $f
        ): array<TKey, TResult> {
            return [];
        }
        """;

    [Theory]
    [InlineData("int $n = Runner::first(new CountTask());")]
    [InlineData("string $s = Runner::first(new DerivedTask());")]
    [InlineData("bool $b = Runner::first(new PairTask());")]
    [InlineData("int $a = Runner::pairFirst(new PairTask());")]
    [InlineData("array<int, int> $m = Runner::many([new CountTask()]);")]
    public void ClassImplementingGenericAncestor_InfersTypeArguments(string statement)
    {
        var errors = Errors($"function probe(): void {{ {statement} }}");

        errors.Select(e => $"{e.Code}: {e.Message}").Should().BeEmpty();
    }

    [Fact]
    public void ClassImplementingGenericAncestor_WrongExpectedType_StillReported()
    {
        var errors = Errors("function probe(): void { string $n = Runner::first(new CountTask()); }");

        errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void LeadingExplicitTypeArguments_StaticMethod_InfersTrailingKey()
    {
        var errors = Errors("""
            function probe(): void {
                array<string, int> $items = ['a' => 1];
                array<string, string> $r = Runner::each<int, string>($items, fn(int $i): string => (string) $i);
            }
            """);

        errors.Select(e => $"{e.Code}: {e.Message}").Should().BeEmpty();
    }

    [Fact]
    public void LeadingExplicitTypeArguments_FreeFunction_InfersTrailingKey()
    {
        var errors = Errors("""
            function use2(): void {
                array<string, int> $items = ['a' => 1];
                array<string, string> $r = eachFree<int, string>($items, fn(int $i): string => (string) $i);
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void FullExplicitTypeArguments_StillWork()
    {
        var errors = Errors("""
            function use3(): void {
                array<string, int> $items = ['a' => 1];
                array<string, string> $r = Runner::each<int, string, string>($items, fn(int $i): string => (string) $i);
            }
            """);

        errors.Should().BeEmpty();
    }

    private static List<IDiagnostic> Errors(string usage) =>
        CompileAndCheck("<?tyhp\n" + Declarations + "\n" + usage + "\n")
            .Errors
            .ToList();

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
