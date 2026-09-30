using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story271")]
public class CallableShapeGuardCheckerTests
{
    [Fact]
    public void IsGuard_NarrowsCallableToShape_AndAllowsInvoke()
    {
        var errors = Check("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            function fromCallable(callable $fn): bool {
                if ($fn is Predicate<int>) {
                    return $fn(1);
                }
                return false;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void IsGuard_NarrowsMixedToShape_AndAllowsInvoke()
    {
        var errors = Check("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            function fromMixed(mixed $fn): bool {
                if ($fn is Predicate<int>) {
                    return $fn(1);
                }
                return false;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void InstanceofGuard_NarrowsTheSameAsIs()
    {
        var errors = Check("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            function fromCallable(callable $fn): bool {
                if ($fn instanceof Predicate<int>) {
                    return $fn(1);
                }
                return false;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void AfterIsGuard_WrongArgumentType_Reports4010()
    {
        var errors = Check("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            function fromCallable(callable $fn): bool {
                if ($fn is Predicate<int>) {
                    return $fn("x");
                }
                return false;
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType, Describe(errors));
    }

    [Fact]
    public void AfterIsGuard_AssignsToShapeParameter()
    {
        var errors = Check("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            function take(Predicate<int> $p): bool {
                return $p(0);
            }
            function fromCallable(callable $fn): bool {
                if ($fn is Predicate<int>) {
                    return take($fn);
                }
                return false;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void IsGuard_NarrowsStructAlias()
    {
        var errors = Check("""
            <?tyhp
            type Point = struct {
                float $x;
                float $y;
            };
            function fromArray(array $x): float {
                if ($x is Point) {
                    return $x->x;
                }
                return 0.0;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    private static IReadOnlyList<IDiagnostic> Check(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
