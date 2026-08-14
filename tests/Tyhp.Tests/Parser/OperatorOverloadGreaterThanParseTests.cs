using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Regression tests for bare <c>&gt;</c> in <c>operator</c> / <c>extension operator</c>
/// declarations (ExtDecimal audit §1). <c>&gt;&gt;</c> must remain a two-GT form.
/// </summary>
[Trait("Category", "Parser")]
public class OperatorOverloadGreaterThanParseTests
{
    [Fact]
    public void Parse_OperatorGreaterThan_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator >(self $left, self $right): bool {
                    return $left->amount > $right->amount;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void Parse_OperatorShiftRight_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Bits {
                public int $v = 0;
                operator >>(self $left, int $right): self {
                    $left->v = $left->v >> $right;
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void Parse_ExtensionOperatorGreaterThan_InTyhpdef_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            namespace Test;
            class Decimal {
                extension operator >(self $left, mixed $right): bool;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void Parse_OperatorPlusWithGenericTarget_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension Ops {
                operator +<\DateTimeImmutable>(\DateTimeImmutable $left, int $right): \DateTimeImmutable {
                    return $left;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
    }

    private static string Describe(ParseResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
