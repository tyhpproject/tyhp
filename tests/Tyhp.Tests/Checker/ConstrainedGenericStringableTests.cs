using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Xunit;

namespace Tyhp.Tests.Checker;

/// <summary>A generic parameter is stringable when its constraint is (TYHP4120).</summary>
[Trait("Category", "Checker")]
public class ConstrainedGenericStringableTests
{
    [Fact]
    public void ConstrainedToStringableTypes_InterpolatesConcatenatesAndEchoes()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function tag<T extends 'a'|'b'>(T $value): string { return "[{$value}]"; }
            function prefixed<T extends int|string>(T $value): string { return 'v' . $value; }
            function show<T extends \Stringable>(T $value): void { echo $value; }
            function showKey<T extends 'a'|'b'>(T $value): void { echo $value; }
            function nested<T extends string, U extends T>(U $value): string { return "{$value}!"; }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void ConstrainedGeneric_FitsTemplateHole()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function wrapped<T extends 'a'|'b'>(T $value): "[${'a'|'b'}]" { return "[{$value}]"; }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void UnconstrainedOrNonStringableConstraint_StillReports()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function anyValue<T>(T $value): string { return "[{$value}]"; }
            function listValue<T extends array>(T $value): string { return "[{$value}]"; }
            function mixedBound<T extends int|array>(T $value): string { return "[{$value}]"; }
            """);

        errors.Should().HaveCount(3, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerConcatNonStringable);
    }
}
