using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>mixed</c> is an unknown value: it binds callee generics like any other argument type, is
/// checked against their constraints, and cannot be laundered through a same-named type parameter
/// of the caller. Unbound callee generics keep the parameter's outer shape.
/// </summary>
public class MixedUnknownGenericTests
{
    [Fact]
    public void CallerAndCalleeShareGenericName_MixedIsNotLaunderedThroughT()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function wrap<T>(T $x): T { return $x; }
            function caller<T>(mixed $m): T { return wrap($m); }
            """);

        Assert.Contains(errors, error => error.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void MixedArgument_BindsGeneric_AndFailsConstraint()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function needInt<T extends int>(T $v): T { return $v; }
            function f(mixed $m): void { needInt($m); }
            """);

        Assert.Contains(errors, error => error.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void MixedArgument_FailsNullableConstraint()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function needMaybeInt<T extends ?int>(T $v): void {}
            function f(mixed $m): void { needMaybeInt($m); }
            """);

        Assert.Contains(errors, error => error.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void NarrowedArgument_SatisfiesConstraint()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function needInt<T extends int>(T $v): T { return $v; }
            function f(mixed $m): void { if ($m is int) { needInt($m); } }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void UnboundGenericArrayParameter_RejectsNonArray()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function first<TKey extends int|string, TValue>(array<TKey, TValue> $a): void {}
            function f(array<int, string> $a): void { first($a); first(5); }
            """);

        var mismatch = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, mismatch.Code);
    }

    [Fact]
    public void UnboundGenericCallableParameter_RejectsNonCallable_AcceptsClosures()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function run<T>(callable(T): T $cb): void {}
            function f(): void { run(5); run(fn(int $x): int => $x); }
            """);

        var mismatch = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, mismatch.Code);
    }

    [Fact]
    public void UnboundGenericClassParameter_RejectsOtherClass()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Box<T> { public function __construct(public T $value) {} }
            class Other {}
            function take<T>(Box<T> $b): void {}
            function f(Box<int> $b, Other $o): void { take($b); take($o); }
            """);

        var mismatch = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, mismatch.Code);
    }
}
