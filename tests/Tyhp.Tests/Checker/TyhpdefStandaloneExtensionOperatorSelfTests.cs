using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A standalone <c>.tyhpdef</c> extension operator is not inside a class.
/// <c>self</c> in its own signature means the block target, the same seed
/// <c>ExtensionRule.CheckExtensionOperatorOverload</c> gives a <c>.tyhp</c> operator.
/// </summary>
[Trait("Category", "Checker")]
public class TyhpdefStandaloneExtensionOperatorSelfTests
{
    [Fact]
    public void IncludedOperator_SelfParameterAndReturn_MeanBlockTarget()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            class Money {
                public int $amount;
            }
            extension MoneyOperators extends Money {
                operator convert(self $value): int => $value->amount;
                operator +(self $left, int $right): self => $left;
            }
            """);

        var fromTyhpdef = errors
            .Where(d => (d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        fromTyhpdef.Should().NotContain(
            d => d.Code == MessageCode.CheckerRelativeTypeOutsideClass,
            "self in the operator signature is the block target Money, not a relative type outside a class");
        fromTyhpdef.Should().BeEmpty(
            "unexpected tyhpdef errors: " + string.Join(", ", fromTyhpdef.Select(e => $"{e.Code}: {e.Message}")));
    }

    /// <summary>
    /// A convert-from operator has no <c>self</c> parameter at all — only the return type is
    /// <c>self</c>. The seed must resolve that return-only use too.
    /// </summary>
    [Fact]
    public void IncludedConvertFromOperator_SelfOnlyAsReturn_MeansBlockTarget()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            class Money {
                public int $amount;
            }
            extension MoneyOperators extends Money {
                operator convert(int $value): self => $value;
            }
            """);

        AssertNoTyhpdefErrors(errors);
    }

    /// <summary>
    /// Each nested <c>extends&lt;T&gt;</c> group is its own block target. The seed must give
    /// <c>self</c> in one group's operator the group's own target, not the sibling group's.
    /// </summary>
    [Fact]
    public void IncludedNestedGroupOperators_SelfMeansEachGroupsOwnTarget()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            class Money {
                public int $amount;
            }
            class Weight {
                public int $grams;
            }
            extension MixedOperators {
                extends Money {
                    operator convert(self $value): int => $value->amount;
                }
                extends Weight {
                    operator convert(self $value): int => $value->grams;
                }
            }
            """);

        AssertNoTyhpdefErrors(errors);
    }

    /// <summary>
    /// The shared seed must not change a <c>.tyhp</c> extension operator's target or check it
    /// twice — a header-target operator and a nested-group operator both stay error-free.
    /// </summary>
    [Fact]
    public void TyhpExtensionOperator_StillResolvesItsOwnTarget()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators extends Money {
                operator convert(self $value): int => $value->amount;
                operator +(self $left, int $right): self => $left;
            }
            function demo(): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            "a .tyhp extension operator must keep resolving self to its own extends target: "
            + string.Join(", ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    /// <summary>
    /// <c>static</c> and <c>parent</c> stay illegal in a standalone tyhpdef extension operator
    /// signature even though <c>self</c> is now legal — the seed must not loosen those too.
    /// </summary>
    [Fact]
    public void IncludedOperator_ParentParameter_StillReportsError()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            class Money {
                public int $amount;
            }
            extension MoneyOperators extends Money {
                operator convert(parent $value): int => 0;
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerParentWithoutParent,
            "parent has no meaning in an extension operator and must still be rejected: "
            + string.Join(", ", errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    /// <summary>
    /// <c>static::</c> / <c>parent::</c> qualifiers in the operator body stay TYHP4368
    /// (<see cref="MessageCode.CheckerExtensionRelativeType"/>) regardless of the <c>self</c> seed.
    /// </summary>
    [Fact]
    public void IncludedOperator_StaticAndParentQualifiersInBody_StillReportError()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            class Money {
                public int $amount;
                public static int $rate;
            }
            extension MoneyOperators extends Money {
                operator convert(self $value): int => static::method($value);
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExtensionRelativeType,
            "static:: inside the operator body must still be rejected: "
            + string.Join(", ", errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    private static void AssertNoTyhpdefErrors(IReadOnlyList<IDiagnostic> errors)
    {
        var fromTyhpdef = errors
            .Where(d => (d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        fromTyhpdef.Should().BeEmpty(
            "unexpected tyhpdef errors: " + string.Join(", ", fromTyhpdef.Select(e => $"{e.Code}: {e.Message}")));
    }

    private static IReadOnlyList<IDiagnostic> IncludeTyhpdef(string tyhpdef)
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            tyhpdef,
            skipChecking: false);

        return result.Diagnostics.Errors;
    }
}
