using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// The right operand of <c>$v = …</c> is checked under <c>$v</c>'s narrowing from before the
/// assignment; the assignment only changes the variable afterwards.
/// </summary>
public class SelfAssignmentNarrowingTests
{
    [Fact]
    public void InstanceofNarrowedVariable_CanBeReassignedFromItsOwnMember()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class T {}
            final class N extends T {
                public function next(): T { return $this; }
            }
            function unwrap(T $b): T {
                if ($b instanceof N) {
                    $b = $b->next();
                }
                return $b;
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void AfterReassignment_NarrowingFromBeforeIsGone()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class T {}
            final class N extends T {
                public function next(): T { return $this; }
            }
            function unwrap(T $b): T {
                if ($b instanceof N) {
                    $b = $b->next();
                    $b = $b->next();
                }
                return $b;
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal(Tyhp.Domain.Exceptions.MessageCode.CheckerSymbolNameNotFound, error.Code);
    }

    [Fact]
    public void GuardedMixedVariable_CanBeReassignedFromItself()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function bump(mixed $m): int {
                if (\is_int($m)) {
                    $m = $m + 1;
                    return $m;
                }
                return 0;
            }
            """);

        Assert.Empty(errors);
    }
}
