using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Xunit;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>.</c> and <c>.=</c> stringify their operands like interpolation and <c>echo</c> (TYHP4120); a
/// <c>null</c> operand concatenates as <c>''</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ConcatStringableTests
{
    [Fact]
    public void NonStringableOperands_Report()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function left(array $items): string { return $items . '!'; }
            function right(array $items): string { return 'v' . $items; }
            function bound<T extends array>(T $value): string { return 'v' . $value; }
            function unbounded<T>(T $value): string { return 'v' . $value; }
            function appended(array $items): string { $s = 'v'; $s .= $items; return $s; }
            function nullableItems(?array $items): string { return 'v' . $items; }
            """);

        errors.Should().HaveCount(6, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerConcatNonStringable);
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("`array`"));
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("`T`"));
    }

    [Fact]
    public void StringableOperands_DoNotReport()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Name { public function __toString(): string { return 'n'; } }
            function scalars(int $n, float $f, bool $b, string $s): string { return $s . $n . $f . $b; }
            function stringable(\Stringable $v, Name $name): string { return 'v' . $v . $name; }
            function constrained<T extends 'a'|'b'>(T $value): string { return 'v' . $value; }
            function chained<T extends string, U extends T>(U $value): string { $s = ''; $s .= $value; return $s; }
            function nullable(?string $name, int|null $n): string { $s = 'v' . $name . null; $s .= $n; return $s; }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void ClassOperandWithoutToString_ReportsOperatorOnlyOnce()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Plain {}
            function joined(Plain $p): string { return 'v' . $p; }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().NotBe(MessageCode.CheckerConcatNonStringable);
    }
}
