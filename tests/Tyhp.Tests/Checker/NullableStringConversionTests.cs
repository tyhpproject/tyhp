using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Xunit;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Concatenation, interpolation, heredoc holes, <c>echo</c>, and <c>print</c> share one string
/// conversion check (TYHP4120): PHP converts <c>null</c> to <c>''</c>, so a nullable value is
/// accepted when its non-null part stringifies. A null hole contributes <c>''</c> to the
/// interpolation's template-string type.
/// </summary>
[Trait("Category", "Checker")]
public class NullableStringConversionTests
{
    [Fact]
    public void NullableStringableValues_AreAcceptedAtEveryConversionSite()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Name { public function __toString(): string { return 'n'; } }
            function interpolated(?string $s, ?int $n): string { return "s={$s} n=$n"; }
            function heredoc(?string $s, ?int $n): string {
                return <<<TXT
                    s={$s} n=$n
                    TXT;
            }
            function echoed(?string $s, ?int $n, float|null $f): void { echo $s, $n, $f; }
            function printed(?string $s, ?int $n): void { print $s; print $n; }
            function concatenated(?string $s, ?int $n): string { return $s . $n; }
            function objects(?Name $name, ?\Stringable $v): string {
                echo $name, $v;
                print $name;
                return "{$name}{$v}" . $name . $v;
            }
            function constrained<T extends string>(?T $value): string { echo $value; return "[{$value}]"; }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void NullableNonStringableValues_StillReportAtEverySite()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function interpolated(?array $a): string { return "v={$a}"; }
            function heredoc(?array $a): string {
                return <<<TXT
                    v={$a}
                    TXT;
            }
            function echoed(?array $a): void { echo $a; }
            function printed(?array $a): void { print $a; }
            function concatenated(?array $a): string { return 'v' . $a; }
            function unconstrained<T>(?T $value): string { return "[{$value}]"; }
            """);

        errors.Should().HaveCount(6, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerConcatNonStringable);
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("array"));
    }

    [Fact]
    public void NullableNonStringableObject_ReportsTyhp4120OutsideConcat()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Plain {}
            function echoed(?Plain $p): void { echo $p; }
            function printed(?Plain $p): void { print $p; }
            function interpolated(?Plain $p): string { return "v={$p}"; }
            """);

        errors.Should().HaveCount(3, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void NullableHole_DoesNotFitHolesThatNeedText()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = 'GET'|'POST';
            type ItemKey = "item-${int}";
            type Route = "${ApiMethod} /items";
            type Label = "${string}";

            function keyFor(?int $id): ItemKey { return "item-{$id}"; }
            function routeFor(?ApiMethod $method): Route { return "{$method} /items"; }
            function labelFor(?string $text): Label { return "{$text}"; }
            """);

        errors.Should().HaveCount(3, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void NullableHole_FitsHolesThatAcceptEmptyText()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type MaybeKey = "item-${''|int}";
            type Tagged = "tag:${string}";

            function keyFor(?int $id): MaybeKey { return "item-{$id}"; }
            function tagFor(?string $text): Tagged { return "tag:x{$text}"; }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }
}
