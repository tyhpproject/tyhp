using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Real source <c>yield</c> / <c>yield from</c> parse as <c>PhpUnaryOpAst</c>, not
/// <c>PhpYieldAst</c>. ControlFlowRule must run TYHP4086 / 4088 / 4089 on those sites.
/// </summary>
[Trait("Category", "Checker")]
public class YieldControlFlowTests
{
    [Fact]
    public void Yield_InsideGenerator_DoesNotReport4086()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                yield 1;
                yield 2 => "a";
                yield;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "yield inside a function that contains yield is a generator: " + Describe(errors));
    }

    [Fact]
    public void Yield_AtFileScope_Reports4086()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            yield 1;
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "file-level yield must report TYHP4086: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_AtFileScope_Reports4086()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            yield from [1, 2];
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "file-level yield from must report TYHP4086: " + Describe(errors));
    }

    [Fact]
    public void Yield_InFinally_Reports4088()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                try {
                    yield 1;
                } finally {
                    yield 2;
                }
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldInFinally,
            "yield in finally must report TYHP4088: " + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "the generator itself is valid; only the finally yield is illegal: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_InFinally_Reports4088()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                try {
                    yield 1;
                } finally {
                    yield from [2];
                }
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldInFinally,
            "yield from in finally must report TYHP4088: " + Describe(errors));
    }

    [Fact]
    public void Yield_InCatch_DoesNotReport4088()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                try {
                    yield 1;
                } catch (\Throwable $e) {
                    yield 2;
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldInFinally,
            "yield in catch is legal: " + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "catch yield is still inside the generator: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_NonIterableInt_Reports4089()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                yield from 123;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from int must report TYHP4089: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_NonIterableString_Reports4089()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                yield from "abc";
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from string must report TYHP4089 (PHP TypeError; foreach-string is not yield-from): "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_ArrayLiteral_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                yield from [1, 2, 3];
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from array literal must be iterable: " + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "yield from inside a generator must not report 4086: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_TypedArrayParam_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(array<int> $xs): \Generator {
                yield from $xs;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from array<int> must be iterable: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_BareGenerator_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(\Generator $inner): \Generator {
                yield from $inner;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from \\Generator must be iterable: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_GenericGenerator_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(\Generator<int, string, mixed, mixed> $inner): \Generator {
                yield from $inner;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from Generator<K,V,…> must unwrap the generic wrapper and count as Traversable: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_IterableParam_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(iterable $xs): \Generator {
                yield from $xs;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from iterable must be accepted: " + Describe(errors));
    }

    [Fact]
    public void Yield_InGeneratorClosure_DoesNotReport4086()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function outer(): void {
                $gen = function(): \Generator {
                    yield 1;
                };
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "a closure that contains yield is its own generator: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_ExpressionAssignment_NonIterable_Reports4089()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                $x = yield from 0;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from in expression position must still report TYHP4089: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_AsCallArgumentInsideReturn_NonIterable_Reports4089()
    {
        // `return useIt(yield from …)` — CheckCall CheckNodes non-closure arguments even
        // though PhpDereferenceableAst suppresses the generic child walk.
        var errors = CompileAndCheck("""
            <?tyhp
            function useIt(mixed $x): mixed {
                return $x;
            }

            function gen(): \Generator {
                return useIt(yield from 123);
            }
            """);

        errors.Should().ContainSingle(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from inside a call argument inside a return statement must report TYHP4089 exactly once: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_AsCallArgument_NonIterable_Reports4089()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useIt(mixed $x): mixed {
                return $x;
            }

            function gen(): \Generator {
                useIt(yield from 123);
            }
            """);

        errors.Should().ContainSingle(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from as a bare call-argument statement must report TYHP4089 exactly once: "
            + Describe(errors));
    }

    [Fact]
    public void Yield_AsCallArgument_InFinally_Reports4088()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useIt(mixed $x): mixed {
                return $x;
            }

            function gen(): \Generator {
                try {
                    yield 1;
                } finally {
                    useIt(yield 2);
                }
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldInFinally,
            "yield as a call argument in finally must report TYHP4088: "
            + Describe(errors));
        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "the generator itself is valid; only the finally yield is illegal: "
            + Describe(errors));
    }

    [Fact]
    public void YieldKeyed_AsCallArgument_InFinally_Reports4088()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useIt(mixed $x): mixed {
                return $x;
            }

            function gen(): \Generator {
                try {
                    yield 1;
                } finally {
                    useIt(yield 2 => "a");
                }
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldInFinally,
            "keyed yield as a call argument in finally must report TYHP4088: "
            + Describe(errors));
    }

    [Fact]
    public void Yield_AsCallArgument_AtFileScope_Reports4086()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function useIt(mixed $x): mixed {
                return $x;
            }

            useIt(yield 1);
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldOutsideGenerator,
            "yield as a bare call argument at file scope must report TYHP4086: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_CustomIteratorClass_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class MyIterator implements \Iterator {
                public function current(): mixed { return null; }
                public function key(): mixed { return null; }
                public function next(): void {}
                public function rewind(): void {}
                public function valid(): bool { return false; }
            }

            function gen(MyIterator $it): \Generator {
                yield from $it;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from a custom \\Iterator implementation must be accepted: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_UnionOfIterables_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(array<int>|\Generator $xs): \Generator {
                yield from $xs;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from array<int>|\\Generator must accept every iterable union member: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_UnionWithNonIterableMember_Reports4089()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(array<int>|int $xs): \Generator {
                yield from $xs;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from array<int>|int must reject the non-iterable union member: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_NullableArray_Reports4089()
    {
        // PHP throws a TypeError on `yield from null` — a nullable operand must not be
        // unwrapped/accepted the way a nullable `foreach` subject would be.
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(?array<int> $xs): \Generator {
                yield from $xs;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerYieldFromNonIterable,
            "yield from a nullable array must report TYHP4089 (PHP TypeError on null): "
            + Describe(errors));
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
