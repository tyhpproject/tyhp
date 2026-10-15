using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Generic call inference: element type arguments merge, a union argument must
/// fit every arm, and a type parameter that cannot be inferred uses the expected
/// type, then its default, then TYHP4377.
/// </summary>
[Trait("Category", "Checker")]
public class GenericCallInferenceGapTests
{
    [Fact]
    public void ArrayOfGeneric_MergesElementTypeArguments()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Cell<T> {
                public function __construct(public T $value) {}
            }

            function first<T>(array<Cell<T>> $cells): T {
                throw new \Exception("unused");
            }

            function demo(Cell<int> $a, Cell<string> $b): int|string {
                return first([$a, $b]);
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void ArrayOfGeneric_MergedResult_IsNotTheFirstElement()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Cell<T> {
                public function __construct(public T $value) {}
            }

            function first<T>(array<Cell<T>> $cells): T {
                throw new \Exception("unused");
            }

            function demo(Cell<int> $a, Cell<string> $b): int {
                return first([$a, $b]);
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void VariadicGeneric_MergesElementTypeArguments()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Cell<T> {
                public function __construct(public T $value) {}
            }

            function fan<T>(Cell<T> ...$cells): T {
                throw new \Exception("unused");
            }

            function demo(Cell<int> $a, Cell<string> $b): int|string {
                return fan($a, $b);
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void VariadicGeneric_MergedResult_IsNotTheFirstElement()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Cell<T> {
                public function __construct(public T $value) {}
            }

            function fan<T>(Cell<T> ...$cells): T {
                throw new \Exception("unused");
            }

            function demo(Cell<int> $a, Cell<string> $b): int {
                return fan($a, $b);
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void UnionArgument_MemberThatMatchesNoArm_Reports4010()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public ?T $v = null) {}
            }

            function f<T>(null|T|Box<T> $v): Box<T> {
                return new Box<T>();
            }

            function demo(Box<int>|string $u): void {
                $r = f($u);
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(result));
    }

    [Fact]
    public void UnionArgument_EveryMemberFits_IsAccepted()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public ?T $v = null) {}
            }

            function f<T>(null|T|Box<T> $v): Box<T> {
                return new Box<T>();
            }

            function demo(Box<int>|int $u): Box<int> {
                return f($u);
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void UnboundTypeParameter_ReportsCannotInfer()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public ?T $v = null) {}
            }

            function make<T>(): Box<T> {
                return new Box<T>();
            }

            function demo(): void {
                $a = make();
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerCannotInferTypeArgument
                && e.Message.Contains('T')
                && e.Message.Contains("make"),
            Describe(result));
    }

    [Fact]
    public void ExpectedType_BindsUnboundTypeParameter()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public ?T $v = null) {}
            }

            function make<T>(): Box<T> {
                return new Box<T>();
            }

            function demo(): Box<int> {
                Box<int> $ctx = make();
                return make();
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void DefaultTypeParameter_UsedWhenNothingElseBinds()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public ?T $v = null) {}
            }

            function makeD<T = int>(): Box<T> {
                return new Box<T>();
            }

            function bare<T = int>(): T {
                throw new \Exception("unused");
            }

            function demo(): void {
                Box<int> $d = makeD();
                int $e = bare();
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
