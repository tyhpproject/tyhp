using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Spread arguments (<c>f(...$xs)</c>) are checked element-wise against the positional
/// parameters they can fill, not only for iterability.
/// </summary>
public class SpreadArgumentTypeTests
{
    private static IReadOnlyList<(MessageCode Code, string Message)> Check(string body) =>
        MixedCheckSnippet.Errors($$"""
            <?tyhp
            function f(int $a, int $b): void {}
            function h(int ...$rest): void {}
            function mixedTail(int $a, string $b): void {}
            {{body}}
            """);

    [Fact]
    public void MatchingElementType_IsAccepted()
    {
        var errors = Check("function t(array<int, int> $xs): void { f(...$xs); h(...$xs); }");

        Assert.Empty(errors);
    }

    [Fact]
    public void WrongElementType_IsRejected()
    {
        var errors = Check("function t(array<int, string> $ys): void { f(...$ys); }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, error.Code);
    }

    [Fact]
    public void WrongElementTypeForVariadic_IsRejected()
    {
        var errors = Check("function t(array<int, string> $ys): void { h(...$ys); }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, error.Code);
    }

    [Fact]
    public void ElementMustFitEveryRemainingPositionalParameter()
    {
        var errors = Check("function t(array<int, int> $xs): void { mixedTail(...$xs); }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, error.Code);
    }

    [Fact]
    public void BareArray_ElementsAreMixed_AndRequireNarrowing()
    {
        var errors = Check("function t(array $bare): void { f(...$bare); }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, error.Code);
    }

    [Fact]
    public void StringKeyedSpread_BindsByNameAndIsNotCheckedPositionally()
    {
        var errors = Check("function t(array<string, string> $named): void { f(...$named); }");

        Assert.Empty(errors);
    }
}
