using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>foreach (await $x as …)</c> over a class that implements <c>AsyncIterable&lt;T&gt;</c>
/// (not only a value whose static type is the interface itself).
/// </summary>
[Trait("Category", "Checker")]
public class AsyncIterableClassForeachTests
{
    private const string Contracts = """
        namespace Tyhp\Contracts {
            interface AsyncIterator<T> {
                public function current(): T;
            }
            interface AsyncKeyValueIterator<TKey, TValue> extends AsyncIterator<TValue> {
                public function currentKey(): TKey;
                public function currentValue(): TValue;
            }
            interface AsyncIterable<T> {
                public function getAsyncIterator(): AsyncIterator<T>;
            }
        }
        namespace App {
            use Tyhp\Contracts\AsyncIterable;
            use Tyhp\Contracts\AsyncIterator;
            use Tyhp\Contracts\AsyncKeyValueIterator;

            final class Lines implements AsyncIterable<string> {
                public function getAsyncIterator(): AsyncIterator<string> {
                    throw new \LogicException('unused');
                }
            }
            final class Pairs implements AsyncIterable<int> {
                public function getAsyncIterator(): AsyncKeyValueIterator<string, int> {
                    throw new \LogicException('unused');
                }
            }
            abstract class BaseLines<T> implements AsyncIterable<T> {}
            final class IntLines extends BaseLines<int> {
                public function getAsyncIterator(): AsyncIterator<int> {
                    throw new \LogicException('unused');
                }
            }
        """;

    [Fact]
    public void ClassImplementingAsyncIterable_ValueIsTypeArgument()
    {
        var errors = Errors("""
            async function run(Lines $l): void {
                foreach (await $l as $line) {
                    string $s = $line;
                    echo $s;
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ClassImplementingAsyncIterable_ValueIsNotAssignableToWrongType()
    {
        var errors = Errors("""
            async function run(Lines $l): void {
                foreach (await $l as $line) {
                    int $n = $line;
                    echo $n;
                }
            }
            """);

        errors.Should().Contain(e => e.Contains("TYHP4008", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassWithKeyValueIterator_KeyComesFromIteratorType()
    {
        var errors = Errors("""
            async function run(Pairs $p): void {
                foreach (await $p as $k => $v) {
                    string $key = $k;
                    int $val = $v;
                    echo $key, $val;
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void InheritedAsyncIterable_ValueResolvesThroughParent()
    {
        var errors = Errors("""
            async function run(IntLines $l): void {
                foreach (await $l as $n) {
                    int $x = $n;
                    echo $x;
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void AsyncIterableInterfaceType_StillWorks()
    {
        var errors = Errors("""
            async function run(AsyncIterable<string> $l): void {
                foreach (await $l as $line) {
                    string $s = $line;
                    echo $s;
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void NonAsyncClass_IsStillRejected()
    {
        var errors = Errors("""
            final class Plain {}
            async function run(Plain $p): void {
                foreach (await $p as $x) {
                    echo 1;
                }
            }
            """);

        errors.Should().Contain(e => e.Contains("TYHP4047", StringComparison.Ordinal));
    }

    private static List<string> Errors(string usage) =>
        CompileAndCheck("<?tyhp\n" + Contracts + "\n" + usage + "\n}\n")
            .Errors
            .Select(e => $"TYHP{(int)e.Code}: {e.Message}")
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
