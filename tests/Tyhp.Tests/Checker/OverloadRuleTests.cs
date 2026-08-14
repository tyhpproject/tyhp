using NSubstitute;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class OverloadRuleTests
{
    private static ParameterInfo Required(string name) =>
        new(name, DeclaredType: null, DefaultValue: null, IsVariadic: false, IsByReference: false, PromotedVisibility: MemberModifier.None);

    private static ParameterInfo Optional(string name) =>
        new(name, DeclaredType: null, DefaultValue: Substitute.For<IExpression>(), IsVariadic: false, IsByReference: false, PromotedVisibility: MemberModifier.None);

    private static ParameterInfo Variadic(string name) =>
        new(name, DeclaredType: null, DefaultValue: null, IsVariadic: true, IsByReference: false, PromotedVisibility: MemberModifier.None);

    [Fact]
    public void IsArityCompatible_FewerParamsThanImplementationWithTrailingDefault_IsAccepted()
    {
        // implementation: foo(int $a, int $b = 0); overload: foo(int $a)
        var implementation = new List<ParameterInfo> { Required("a"), Optional("b") };

        OverloadRule.IsArityCompatible(1, implementation).Should().BeTrue();
        OverloadRule.IsArityCompatible(2, implementation).Should().BeTrue();
    }

    [Fact]
    public void IsArityCompatible_FewerThanRequiredCount_IsRejected()
    {
        var implementation = new List<ParameterInfo> { Required("a"), Required("b"), Optional("c") };

        OverloadRule.IsArityCompatible(1, implementation).Should().BeFalse();
        OverloadRule.IsArityCompatible(2, implementation).Should().BeTrue();
        OverloadRule.IsArityCompatible(3, implementation).Should().BeTrue();
    }

    [Fact]
    public void IsArityCompatible_MoreThanTotal_NonVariadic_IsRejected()
    {
        var implementation = new List<ParameterInfo> { Required("a"), Optional("b") };

        OverloadRule.IsArityCompatible(3, implementation).Should().BeFalse();
    }

    [Fact]
    public void IsArityCompatible_VariadicImplementation_AcceptsExtraParams()
    {
        var implementation = new List<ParameterInfo> { Required("a"), Variadic("rest") };

        OverloadRule.IsArityCompatible(1, implementation).Should().BeTrue();
        OverloadRule.IsArityCompatible(5, implementation).Should().BeTrue();
        OverloadRule.IsArityCompatible(0, implementation).Should().BeFalse();
    }

    [Fact]
    public void IsArityCompatible_ExactMatch_IsAccepted()
    {
        var implementation = new List<ParameterInfo> { Required("a"), Required("b") };

        OverloadRule.IsArityCompatible(2, implementation).Should().BeTrue();
        OverloadRule.IsArityCompatible(1, implementation).Should().BeFalse();
    }
}
