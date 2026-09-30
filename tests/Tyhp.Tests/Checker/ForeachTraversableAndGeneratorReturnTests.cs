using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Foreach key/value types must come from the iterated type's
/// <c>Iterator</c>/<c>IteratorAggregate</c> contract (not its own generic positions), and
/// <c>return</c> inside a generator must check against <c>TReturn</c>. Story 21 Phase 3
/// ControlFlowRule cluster in FOUND_BUGS.md.
/// </summary>
[Trait("Category", "Checker")]
public class ForeachTraversableAndGeneratorReturnTests
{
    [Fact]
    public void Foreach_Generator_UsesTKeyAndTValue_NotTReturn()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useGenerator(\Generator<int, string, mixed, mixed> $gen): void {
                foreach ($gen as $key => $value) {
                    int $checkKey = $key;
                    string $checkValue = $value;
                }
            }
            """);

        errors.Should().BeEmpty(
            "foreach over Generator<int,string,…> must type key=int value=string: " + Describe(errors));
    }

    [Fact]
    public void Foreach_SplPriorityQueue_UsesIteratorIntAndTValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useSplPriorityQueue(\SplPriorityQueue<int, string> $q): void {
                foreach ($q as $key => $value) {
                    int $checkKey = $key;
                    string $checkValue = $value;
                }
            }
            """);

        errors.Should().BeEmpty(
            "foreach over SplPriorityQueue<int,string> must type key=int value=string: "
            + Describe(errors));
    }

    [Fact]
    public void Foreach_Array_StillUsesPositionalKeyValue()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useArray(array<string, int> $xs): void {
                foreach ($xs as $key => $value) {
                    string $checkKey = $key;
                    int $checkValue = $value;
                }
            }
            """);

        errors.Should().BeEmpty(
            "foreach over array<K,V> must keep positional key/value: " + Describe(errors));
    }

    [Fact]
    public void Foreach_ArrayOfStruct_TypesValueAsStruct()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function sum(array<Money> $amounts): int {
                int $total = 0;
                foreach ($amounts as $m) {
                    $total += $m->cents;
                }
                return $total;
            }
            """);

        errors.Should().BeEmpty(
            "foreach over array<Money> must type $m as Money so $m->cents is int: "
            + Describe(errors));
    }

    [Fact]
    public void Foreach_IterableOfStruct_TypesValueAsStruct()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function sum(iterable<Money> $amounts): int {
                int $total = 0;
                foreach ($amounts as $m) {
                    $total += $m->cents;
                }
                return $total;
            }
            """);

        errors.Should().BeEmpty(
            "foreach over iterable<Money> must type $m as Money so $m->cents is int: "
            + Describe(errors));
    }

    [Fact]
    public void Foreach_ArrayOfStruct_UnknownMember_Reports4101()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function sum(array<Money> $amounts): int {
                int $total = 0;
                foreach ($amounts as $m) {
                    $total += $m->doesNotExist;
                }
                return $total;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerSymbolNameNotFound,
            "foreach over array<Money> must diagnose $m->doesNotExist: " + Describe(errors));
    }

    [Fact]
    public void Generator_ReturnValue_AgainstBareGenerator_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useGenerator(): \Generator {
                yield 1 => "a";
                return 0;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            "return inside generator must not be checked against \\Generator: " + Describe(errors));
        errors.Should().BeEmpty("generator with return payload should typecheck: " + Describe(errors));
    }

    [Fact]
    public void Generator_ReturnValue_AgainstTReturn_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useGenerator(): \Generator<int, string, mixed, int> {
                yield 1 => "a";
                return 0;
            }
            """);

        errors.Should().BeEmpty(
            "return 0 must satisfy Generator<…, int> TReturn: " + Describe(errors));
    }

    [Fact]
    public void Generator_ReturnValue_AgainstTReturn_RejectedWhenIncompatible()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useGenerator(): \Generator<int, string, mixed, int> {
                yield 1 => "a";
                return "nope";
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            "return string must fail against TReturn=int: " + Describe(errors));
    }

    [Fact]
    public void Generator_NoExplicitReturn_DoesNotReportMissingReturnStatement()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useGenerator(): \Generator {
                yield 1;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerMissingReturnStatement,
            "a generator implicitly returns null from TReturn when it falls off the end — no "
            + "explicit `return` is required: " + Describe(errors));
    }

    [Fact]
    public void Generator_Method_NoExplicitReturn_DoesNotReportMissingReturnStatement()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class C {
                public function gen(): \Generator {
                    yield 1;
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerMissingReturnStatement,
            "a generator method implicitly returns null from TReturn when it falls off the end: "
            + Describe(errors));
    }

    [Fact]
    public void Foreach_CustomInterfaceRemapsGenericParams_UsesConcreteArgs()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            interface MyIter<A, B> extends \Iterator<B, A> {
            }

            class Foo implements MyIter<int, string> {
                public function current(): int { return 1; }
                public function key(): string { return "k"; }
                public function next(): void {}
                public function rewind(): void {}
                public function valid(): bool { return false; }
            }

            function useFoo(Foo $f): void {
                foreach ($f as $key => $value) {
                    string $checkKey = $key;
                    int $checkValue = $value;
                }
            }
            """);

        errors.Should().BeEmpty(
            "foreach over Foo (implements MyIter<int,string> extends Iterator<B,A>) must resolve "
            + "key=string(B) value=int(A) via remapped interface generics: " + Describe(errors));
    }

    [Fact]
    public void Closure_InsideGenerator_DoesNotInheritGeneratorReturnUnwrapping()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function outer(): \Generator {
                yield 1;
                $inner = function(): \Generator {
                    return "not-a-generator";
                };
                return 0;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            "closure declared to return \\Generator but returning a string must still fail even "
            + "though it is lexically inside a generator function: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
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
