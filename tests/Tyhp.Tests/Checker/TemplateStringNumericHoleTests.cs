using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Xunit;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>${int}</c> and <c>${float}</c> holes accept the text PHP's <c>(string)</c> cast produces for a
/// value of that type.
/// </summary>
[Trait("Category", "Checker")]
public class TemplateStringNumericHoleTests
{
    private static IReadOnlyList<string> RejectedLiterals(string declarations, params string[] literals)
    {
        var functions = string.Join(
            "\n",
            literals.Select((literal, i) => $"function f{i}(): Target {{ return '{literal}'; }}"));
        var errors = MixedCheckSnippet.Errors("<?tyhp\n" + declarations + "\n" + functions + "\n");
        errors.Where(e => e.Code != MessageCode.CheckerIncompatibleReturnType)
            .Should().BeEmpty(MixedCheckSnippet.Format(errors));
        return literals
            .Where(literal => errors.Any(e => e.Message.Contains("`'" + literal + "'`")))
            .ToList();
    }

    [Fact]
    public void IntHole_AcceptsCanonicalIntegerText()
    {
        var rejected = RejectedLiterals(
            "type Target = \"${int} items\";",
            "3 items", "-12 items", "0 items", "9223372036854775807 items", "-9223372036854775808 items");

        rejected.Should().BeEmpty();
    }

    [Fact]
    public void IntHole_RejectsNonCanonicalIntegerText()
    {
        string[] literals =
        [
            "03 items", "3.0 items", " items", "-0 items", "+3 items", "1e3 items", "x items",
            "9223372036854775808 items", "3  items",
        ];

        var rejected = RejectedLiterals("type Target = \"${int} items\";", literals);

        rejected.Should().BeEquivalentTo(literals);
    }

    [Fact]
    public void IntHole_WithQuantifier_RepeatsCanonicalIntegers()
    {
        var rejected = RejectedLiterals(
            "type Target = \"v${int}+\";",
            "v1", "v12", "v-1", "v", "v1.2");

        rejected.Should().BeEquivalentTo(["v", "v1.2"]);
    }

    [Fact]
    public void IntLiteralHole_AcceptsOnlyThoseValues()
    {
        var rejected = RejectedLiterals(
            "type Target = \"page-${1|2}\";",
            "page-1", "page-2", "page-3", "page-01");

        rejected.Should().BeEquivalentTo(["page-3", "page-01"]);
    }

    [Fact]
    public void FloatHole_AcceptsPhpFloatToStringText()
    {
        var rejected = RejectedLiterals(
            "type Target = \"${float}\";",
            "1.5", "3", "-0.25", "0.1", "1.0E+25", "1.0E-5", "-0", "0.0001", "12345678901234");

        rejected.Should().BeEmpty();
    }

    [Fact]
    public void FloatHole_RejectsTextNoFloatCastProduces()
    {
        string[] literals =
        [
            "1.50", "01.5", "1.5e3", ".5", "1.0e+25", "1E+25", "3.", "", "abc", "0.00001",
            "123456789012345", "1.23456789012345",
        ];

        var rejected = RejectedLiterals("type Target = \"${float}\";", literals);

        rejected.Should().BeEquivalentTo(literals);
    }

    [Fact]
    public void NumericHoles_AcceptInterpolatedValuesOfThatType()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ItemCount = "${int} items";
            type Amount = "${float} EUR";

            function itemsOf(int $n): ItemCount { return "{$n} items"; }
            function fixedItems(): ItemCount { $n = 3; return "{$n} items"; }
            function amountOf(float $p): Amount { return "{$p} EUR"; }
            function wholeAmount(int $p): Amount { return "{$p} EUR"; }
            function fixedAmount(): Amount { $p = 2.50; return "{$p} EUR"; }
            function wrongItems(string $n): ItemCount { return "{$n} items"; }
            function wrongAmount(string $p): Amount { return "{$p} EUR"; }
            """);

        errors.Should().HaveCount(2, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void DocumentedExamples_TypeCheck()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ItemCount = "${int} items";

            function itemsOf(int $n): ItemCount {
                return "{$n} items";
            }

            function firstPage(): "page-${1|2}" {
                return 'page-1';
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void NumericHoleTemplate_IsSubtypeOfStringHoleTemplate()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ItemCount = "${int} items";
            type ItemLabel = "${string} items";
            type Id = "${int}";
            type Digits = "${'1'|'22'} items";

            function toLabel(ItemCount $c): ItemLabel { return $c; }
            function fromLabel(ItemLabel $l): ItemCount { return $l; }
            function toId(ItemCount $c): Id { return $c; }
            function fromDigits(Digits $d): ItemCount { return $d; }
            """);

        errors.Should().HaveCount(2, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }
}
