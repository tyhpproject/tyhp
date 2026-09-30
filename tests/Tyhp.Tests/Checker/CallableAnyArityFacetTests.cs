using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>callable(...): TReturn</c> any-arity facet: parse, assignability, bounds, and
/// inferred generic constraint checking.
/// </summary>
[Trait("Category", "Checker")]
public class CallableAnyArityFacetTests
{
    [Fact]
    public void AnyArity_Parses_AndKnownAritiesStayDistinct()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function zero(callable(): bool $cb): void {
                $cb();
            }
            function one(callable(int $i): bool $cb): void {
                $cb(1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void CallableEllipsisMissingReturn_IsParseError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo<T extends callable<...>>(T $cb): void {}
            """);

        errors.Should().NotBeEmpty(Describe(errors));
        errors.Should().NotContain(e => e.Code == MessageCode.CheckerGenericArgumentCountMismatch
            && e.Message.Contains("callable<", StringComparison.Ordinal),
            Describe(errors));
    }

    [Fact]
    public void CallableEllipsisNotFirstOfTwo_IsParseError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo<T extends callable<int, ..., bool>>(T $cb): void {}
            """);

        errors.Should().NotBeEmpty(Describe(errors));
    }

    [Fact]
    public void ExplicitCallSiteEllipsis_OnUnconstrainedGeneric_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function identity<T>(T $x): T {
                return $x;
            }
            function demo(): void {
                identity<...>(1);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableEllipsisNotAllowed,
            Describe(errors));
    }

    [Fact]
    public void ArrayEllipsisTypeArgument_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(array<..., int> $xs): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableEllipsisNotAllowed,
            Describe(errors));
    }

    [Fact]
    public void CallableEllipsisWithExtraReturnArg_IsParseError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo<T extends callable<..., bool, int>>(T $cb): void {}
            """);

        errors.Should().NotBeEmpty(Describe(errors));
    }

    [Fact]
    public void ClosureEllipsisTypeArgument_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(\Closure<..., bool> $cb): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableEllipsisNotAllowed,
            Describe(errors));
    }

    [Fact]
    public void InvokableObject_MatchingReturn_AssignsToAnyArityBound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Ok {
                public function __invoke(int $n): bool {
                    return true;
                }
            }
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function demo(Ok $h): void {
                pin($h);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void InvokableObject_MismatchedReturn_DoesNotAssignToAnyArityBound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Bad {
                public function __invoke(): int {
                    return 1;
                }
            }
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function demo(Bad $h): void {
                pin($h);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied
            || e.Code == MessageCode.CheckerTypeMismatch
            || e.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(errors));
    }

    [Fact]
    public void MatchingFacets_AssignToAnyArityBound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function one(int $n): bool { return true; }
            function two(string $s, int $n): bool { return true; }
            function demo(): void {
                pin(one(...));
                pin(two(...));
                pin(fn(int $n): bool => true);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void MismatchedReturn_DoesNotAssignToAnyArityBound()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function returnsInt(int $n): int { return $n; }
            function demo(): void {
                pin(returnsInt(...));
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    [Fact]
    public void AnyArity_DoesNotAssignToKnownArity()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function takeZero(callable(): bool $cb): void {
                $cb();
            }
            function takeOne(callable(int $i): bool $cb): void {
                $cb(1);
            }
            function pass<TCallable extends callable(...): bool>(TCallable $cb): void {
                takeZero($cb);
                takeOne($cb);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void OneArgFacet_DoesNotAssignToZeroArgFacet()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function takeZero(callable(): bool $cb): void {
                $cb();
            }
            function one(int $n): bool { return true; }
            function demo(): void {
                takeZero(one(...));
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void AllDefaultedFunction_StillAssignsToZeroArgFacet()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function takeZero(callable(): bool $cb): void {
                $cb();
            }
            function maybe(int $n = 0): bool { return true; }
            function demo(): void {
                takeZero(maybe(...));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void DirectAnyArityParameter_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable(...): bool $cb): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableAnyArityNotAValueType,
            Describe(errors));
    }

    [Fact]
    public void DirectAnyArityReturn_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function make(): callable(...): bool {
                return fn(): bool => true;
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableAnyArityNotAValueType,
            Describe(errors));
    }

    [Fact]
    public void CallingAnyArityValue_IsError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(callable(...): bool $cb): void {
                $cb();
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableAnyArityNotInvokable,
            Describe(errors));
        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableAnyArityNotAValueType,
            Describe(errors));
    }

    [Fact]
    public void TypeParameterBound_AllowsConcreteCallback()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function demo(): void {
                pin(fn(int $n): bool => true);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExplicitBadTypeArgument_AgainstAnyArityBound_Errors()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function demo(): void {
                pin<callable(int $i): int>(fn(int $n): int => $n);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    [Fact]
    public void InferredObjectBound_RejectsString()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Foo {}
            function f<T extends object>(T $x): void {}
            function demo(): void {
                f("hello");
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            Describe(errors));
    }

    [Fact]
    public void CallableReturnType_OfAnyArityFacet_IsReturnType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(__CallableReturnType<callable(...): bool> $t): void {
                $t = true;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void CallableParametersTuple_OfBareAnyArity_StaysEmpty()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(__CallableParametersTuple<callable(...): bool> $args): void {
                $args = [];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void IteratorApplyStyle_TupleFollowsInferredCallable()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function iterator_apply<TKey, TValue, TCallable extends callable(...): bool>(
                \Traversable<TKey, TValue> $iterator,
                TCallable $callback,
                __CallableParametersTuple<TCallable> $args
            ): int {
                return 0;
            }
            function iterator_apply_named<TKey, TValue, TCallable extends callable(...): bool>(
                \Traversable<TKey, TValue> $iterator,
                TCallable $callback,
                __CallableParametersStruct<TCallable> $args
            ): int {
                return 0;
            }
            function demo(\Iterator<int, int> $it): void {
                iterator_apply($it, fn(int $n, string $label): bool => true, [1, 'x']);
                iterator_apply_named(
                    $it,
                    fn(int $n, string $label): bool => true,
                    ['n' => 1, 'label' => 'x']
                );
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtSplOverlay_IteratorApplyAnyArity_Parses()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, """
            <?tyhp
            namespace Test;
            function demo(): void {}
            """);

        // A small standalone stand-in for the `iterator_apply` any-arity-callable overloads in
        // the real `runtime/packages/php/_tyhpdef/overlays/Ext.SPL.tyhpdef` (kept in sync by
        // hand). C# unit tests must not load `runtime/packages`; see `IsolatedCompilation`.
        var overlayPath = Path.Combine(tempDir, "Ext.SPL.overlay.tyhpdef");
        File.WriteAllText(overlayPath, """
            <?tyhpdef
            // @overlay-against: function iterator_apply(\Traversable $iterator, callable $callback, array $args): int
            function iterator_apply<TKey, TValue>(
                \Traversable<TKey, TValue> $iterator,
                callable(): bool $callback,
                null $args = null
            ): int;

            // @overlay-against: function iterator_apply(\Traversable $iterator, callable $callback, array $args): int
            function iterator_apply<TKey, TValue, TCallable extends callable(...): bool>(
                \Traversable<TKey, TValue> $iterator,
                TCallable $callback,
                __CallableParametersTuple<TCallable> $args
            ): int;

            // @overlay-against: function iterator_apply(\Traversable $iterator, callable $callback, array $args): int
            function iterator_apply<TKey, TValue, TCallable extends callable(...): bool>(
                \Traversable<TKey, TValue> $iterator,
                TCallable $callback,
                __CallableParametersStruct<TCallable> $args
            ): int;
            """);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                skipChecking: true,
                tyhpdefIncludePaths: [overlayPath]);
            var result = compilationService.ParseFiles([filePath], options);

            result.Diagnostics.Errors
                .Where(e => (e.FileName ?? "").Contains("Ext.SPL", StringComparison.Ordinal)
                    && (e.Message.Contains("callable", StringComparison.OrdinalIgnoreCase)
                        || e.Code == MessageCode.CheckerCallableEllipsisNotAllowed
                        || e.Message.Contains("...", StringComparison.Ordinal)))
                .Should()
                .BeEmpty(Describe(result.Diagnostics.Errors));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

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
            if (result.GlobalScope is null)
            {
                return result.Diagnostics.Errors
                    .Where(e => e.FileName is not null
                        && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                    .ToList();
            }

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
