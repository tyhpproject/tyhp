using Tyhp.TyhpLang.Versioning;

namespace Tyhp.Tests.Versioning;

[Trait("Category", "Versioning")]
[Trait("Category", "Story20.5")]
public class PhpVersionConstraintTests
{
    [Theory]
    [InlineData("8.2", 8, 2, 0, 2)]
    [InlineData("8.2.0", 8, 2, 0, 3)]
    [InlineData("8.2.15", 8, 2, 15, 3)]
    [InlineData("v8.4", 8, 4, 0, 2)]
    [InlineData(" 8.3 ", 8, 3, 0, 2)]
    public void TryNormalizeTarget_PadsMissingPatch(string input, int major, int minor, int patch, int specified)
    {
        PhpVersionConstraint.TryNormalizeTarget(input, out var version).Should().BeTrue();
        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
        version.SpecifiedComponentCount.Should().Be(specified);
        version.ToString().Should().Be($"{major}.{minor}.{patch}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("8.2.*")]
    [InlineData(">=8.2")]
    [InlineData("not-a-version")]
    public void TryNormalizeTarget_RejectsInvalid(string? input)
    {
        PhpVersionConstraint.TryNormalizeTarget(input, out _).Should().BeFalse();
        PhpVersion.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("8.2.0", "8.2.0", true)]
    [InlineData("8.2", "8.2.0", true)]
    [InlineData("8.2.0", "8.2", true)]
    [InlineData("8.2.1", "8.2.0", false)]
    [InlineData("8.3", "8.2.0", false)]
    public void ExactThreePart_MatchesOnlyThatPatch(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", "8.2", true)]
    [InlineData("8.2.0", "8.2", true)]
    [InlineData("8.2.15", "8.2", true)]
    [InlineData("8.3", "8.2", false)]
    [InlineData("8.1.99", "8.2", false)]
    [InlineData("8.2", "=8.2", true)]
    [InlineData("8.2.7", "=8.2", true)]
    [InlineData("8.3", "=8.2", false)]
    [InlineData("8.2", "==8.2", true)]
    [InlineData("8.2.1", "==8.2", true)]
    [InlineData("8.2", "8", true)]
    [InlineData("8.5", "8", true)]
    [InlineData("9.0", "8", false)]
    public void BareMinor_MatchesEntireMinor(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", ">=8.2", true)]
    [InlineData("8.2.0", ">=8.2", true)]
    [InlineData("8.3", ">=8.2", true)]
    [InlineData("8.5", ">=8.2", true)]
    [InlineData("8.1", ">=8.2", false)]
    [InlineData("8.2", ">=8.2.0", true)]
    [InlineData("8.2", ">=8.3", false)]
    [InlineData("8.2", ">= 8.2", true)]
    public void GreaterThanOrEqual_PadsTargetAndConstraint(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.1", "<8.2", true)]
    [InlineData("8.2", "<8.2", false)]
    [InlineData("8.2.0", "<8.2", false)]
    [InlineData("8.3", "<8.2", false)]
    [InlineData("8.2", "<8.3", true)]
    [InlineData("8.2", "<8.2.1", true)]
    [InlineData("8.4", "<8.4", false)]
    public void LessThan_UsesZeroPaddedBound(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", ">=8.2 <8.4", true)]
    [InlineData("8.3", ">=8.2 <8.4", true)]
    [InlineData("8.4", ">=8.2 <8.4", false)]
    [InlineData("8.1", ">=8.2 <8.4", false)]
    [InlineData("8.3", ">=8.2,<8.4", true)]
    [InlineData("8.4", ">=8.2, <8.4", false)]
    [InlineData("8.2", ">=8.2, <8.3", true)]
    public void AndRanges_RequireEveryComparator(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", ">=8.2 <8.3 || >=8.5", true)]
    [InlineData("8.5", ">=8.2 <8.3 || >=8.5", true)]
    [InlineData("8.4", ">=8.2 <8.3 || >=8.5", false)]
    [InlineData("8.3", ">=8.2 <8.3 || >=8.5", false)]
    [InlineData("8.2", "8.2 || 8.4", true)]
    [InlineData("8.4", "8.2 || 8.4", true)]
    [InlineData("8.3", "8.2 || 8.4", false)]
    [InlineData("8.4", "8.2 | 8.4", true)]
    [InlineData("8.2", ">=8.2||>=8.5", true)]
    public void OrRanges_MatchEitherBranch(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", "^8.2", true)]
    [InlineData("8.5", "^8.2", true)]
    [InlineData("8.2.15", "^8.2.3", true)]
    [InlineData("8.2.0", "^8.2.3", false)]
    [InlineData("9.0", "^8.2", false)]
    [InlineData("8.2", "^8.2.0", true)]
    [InlineData("0.3.1", "^0.3", true)]
    [InlineData("0.4.0", "^0.3", false)]
    [InlineData("0.0.3", "^0.0.3", true)]
    [InlineData("0.0.4", "^0.0.3", false)]
    public void Caret_AllowsNonBreakingUpdates(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", "~8.2", true)]
    [InlineData("8.5", "~8.2", true)]
    [InlineData("9.0", "~8.2", false)]
    [InlineData("8.2", "~8.2.0", true)]
    [InlineData("8.2.15", "~8.2.0", true)]
    [InlineData("8.3", "~8.2.0", false)]
    [InlineData("8.2.3", "~8.2.3", true)]
    [InlineData("8.2.4", "~8.2.3", true)]
    [InlineData("8.3.0", "~8.2.3", false)]
    public void Tilde_AllowsLastSpecifiedDigitToRise(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", "8.2.*", true)]
    [InlineData("8.2.15", "8.2.*", true)]
    [InlineData("8.2", "8.2.x", true)]
    [InlineData("8.3", "8.2.*", false)]
    [InlineData("8.2", "8.*", true)]
    [InlineData("9.0", "8.*", false)]
    [InlineData("8.4", "*", true)]
    [InlineData("8.2", "*.*", true)]
    public void Wildcard_MatchesSpecifiedPrefix(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", "8.2 - 8.4", true)]
    [InlineData("8.4.9", "8.2 - 8.4", true)]
    [InlineData("8.5", "8.2 - 8.4", false)]
    [InlineData("8.1", "8.2 - 8.4", false)]
    [InlineData("8.2", "8.2.0 - 8.4.0", true)]
    [InlineData("8.4.0", "8.2.0 - 8.4.0", true)]
    [InlineData("8.4.1", "8.2.0 - 8.4.0", false)]
    public void HyphenRange_UsesComposerPartialUpperBound(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("8.2", ">=8.2@dev", true)]
    [InlineData("8.2", "8.2@stable", true)]
    [InlineData("8.4", "*@stable", true)]
    [InlineData("8.2", "@dev", true)]
    public void StabilityFlags_AreAcceptedOnNumericPhpConstraints(string target, string constraint, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Theory]
    [InlineData("!=8.2.0", "8.2", false)]
    [InlineData("!=8.2.0", "8.2.1", true)]
    [InlineData("<>8.3.0", "8.2", true)]
    [InlineData(">8.2", "8.2", false)]
    [InlineData(">8.2.0", "8.2.1", true)]
    [InlineData("<=8.2", "8.2", true)]
    [InlineData("<=8.2", "8.3", false)]
    public void OtherComparators_PadZerosWithoutMinorBanding(string constraint, string target, bool expected)
    {
        AssertSatisfied(target, constraint, expected);
    }

    [Fact]
    public void TryParse_DoesNotThrowOnNullOrEmpty()
    {
        PhpVersionConstraint.TryParse(null, out var parsed, out var error).Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();

        PhpVersionConstraint.TryParse("", out parsed, out error).Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();

        PhpVersionConstraint.TryParse("   ", out parsed, out error).Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData(">=")]
    [InlineData(">>8.2")]
    [InlineData("~>8.2")]
    [InlineData(">=8.2 ||")]
    [InlineData("|| 8.2")]
    [InlineData("8.2.2.2.2")]
    [InlineData("dev-master")]
    [InlineData(">=8.2.*")]
    [InlineData("8.2-8.4")]
    [InlineData("^^8.2")]
    [InlineData("foo >=8.2")]
    public void InvalidConstraints_AreFailedParsesNotExceptions(string constraint)
    {
        var act = () => PhpVersionConstraint.TryParse(constraint, out var parsed, out var error);
        act.Should().NotThrow();
        PhpVersionConstraint.TryParse(constraint, out var parsedConstraint, out var parseError).Should().BeFalse();
        parsedConstraint.Should().BeNull();
        parseError.Should().NotBeNullOrWhiteSpace();
        PhpVersionConstraint.IsValid(constraint).Should().BeFalse();

        var result = PhpVersionConstraint.Evaluate("8.2", constraint);
        result.ConstraintIsValid.Should().BeFalse();
        result.IsSatisfied.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        PhpVersionConstraint.IsSatisfied("8.2", constraint).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DoesNotThrowOnNullTargetOrConstraint()
    {
        var act = () => PhpVersionConstraint.Evaluate(null, null);
        act.Should().NotThrow();

        var bothNull = PhpVersionConstraint.Evaluate(null, null);
        bothNull.ConstraintIsValid.Should().BeFalse();
        bothNull.IsSatisfied.Should().BeFalse();

        var nullTarget = PhpVersionConstraint.Evaluate(null, ">=8.2");
        nullTarget.ConstraintIsValid.Should().BeTrue();
        nullTarget.TargetIsValid.Should().BeFalse();
        nullTarget.IsSatisfied.Should().BeFalse();

        PhpVersionConstraint.IsSatisfied(null, ">=8.2").Should().BeFalse();
        PhpVersionConstraint.IsSatisfied("8.2", null).Should().BeFalse();
        PhpVersionConstraint.IsSatisfied("", "").Should().BeFalse();
    }

    [Fact]
    public void TryParse_ThenIsSatisfiedBy_MatchesEvaluate()
    {
        PhpVersionConstraint.TryParse(">=8.2 <8.4", out var parsed).Should().BeTrue();
        parsed.Should().NotBeNull();
        parsed!.Original.Should().Be(">=8.2 <8.4");
        parsed.IsSatisfiedBy("8.3").Should().BeTrue();
        parsed.IsSatisfiedBy("8.4").Should().BeFalse();
        parsed.IsSatisfiedBy(null).Should().BeFalse();
        parsed.IsSatisfiedBy("").Should().BeFalse();
        parsed.IsSatisfiedBy("nope").Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ReportsValidUnsatisfiedSeparatelyFromInvalidSyntax()
    {
        var unsatisfied = PhpVersionConstraint.Evaluate("8.2", ">=8.4");
        unsatisfied.ConstraintIsValid.Should().BeTrue();
        unsatisfied.TargetIsValid.Should().BeTrue();
        unsatisfied.IsSatisfied.Should().BeFalse();
        unsatisfied.Error.Should().BeNull();
        unsatisfied.Constraint.Should().NotBeNull();

        var invalid = PhpVersionConstraint.Evaluate("8.2", "not-a-version");
        invalid.ConstraintIsValid.Should().BeFalse();
        invalid.IsSatisfied.Should().BeFalse();
        invalid.Error.Should().NotBeNull();
        invalid.Constraint.Should().BeNull();
    }

    [Theory]
    [InlineData(">=8.3", ">=8.4", true)]
    [InlineData(">=8.3 <8.4", ">=8.4", false)]
    [InlineData(">=8.3.5 <8.3.10", ">=8.3.7 <8.3.12", true)]
    [InlineData(">=8.3.0 <8.3.5", ">=8.3.5 <8.3.10", false)]
    [InlineData(">=8.2 <8.4", ">=8.4", false)]
    [InlineData("8.2.5", "8.2.5", true)]
    [InlineData("8.2.5", "8.2.6", false)]
    [InlineData(">=8.2", "<8.2", false)]
    [InlineData(">=8.2", "<=8.2", true)]
    [InlineData("*", ">=8.4", true)]
    public void AnyOverlap_SingleConstraintEachSide(string left, string right, bool expected)
    {
        PhpVersionConstraint.AnyOverlap([left], [right]).Should().Be(expected);
        PhpVersionConstraint.AnyOverlap([right], [left]).Should().Be(expected, "overlap must be symmetric");
    }

    [Fact]
    public void AnyOverlap_AndsEachSideBeforeComparing()
    {
        // Enclosing declare(">=8.3") AND-ed with attribute ">=8.4" narrows to ">=8.4",
        // which does not overlap an attribute restricted to "<8.4" on the other declaration.
        PhpVersionConstraint.AnyOverlap([">=8.3", ">=8.4"], ["<8.4"]).Should().BeFalse();
        PhpVersionConstraint.AnyOverlap([">=8.3", ">=8.4"], [">=8.4 <8.5"]).Should().BeTrue();
    }

    [Fact]
    public void AnyOverlap_EmptyListIsUnconstrained()
    {
        PhpVersionConstraint.AnyOverlap([], [">=8.4"]).Should().BeTrue();
        PhpVersionConstraint.AnyOverlap([], []).Should().BeTrue();
    }

    [Fact]
    public void AnyOverlap_InvalidConstraintContributesNoRestriction()
    {
        PhpVersionConstraint.AnyOverlap(["not-a-version"], [">=8.4"]).Should().BeTrue();
        PhpVersionConstraint.AnyOverlap(["not-a-version"], ["<8.0"]).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void SupportedTargets_EvaluateTypicalStoryGates(string target)
    {
        AssertSatisfied(target, ">=8.2", true);
        AssertSatisfied(target, ">=8.3", target is not "8.2");
        AssertSatisfied(target, ">=8.4", target is "8.4" or "8.5");
        AssertSatisfied(target, ">=8.5", target is "8.5");
        AssertSatisfied(target, ">=8.2 <8.4", target is "8.2" or "8.3");
        AssertSatisfied(target, ">=8.3 <8.4", target is "8.3");
        AssertSatisfied(target, ">=8.4 <8.5", target is "8.4");
        AssertSatisfied(target, "^8.2", true);
        AssertSatisfied(target, "~8.2", true);
        AssertSatisfied(target, "8.2 || 8.3 || 8.4 || 8.5", true);
    }

    private static void AssertSatisfied(string target, string constraint, bool expected)
    {
        PhpVersionConstraint.IsValid(constraint).Should().BeTrue($"constraint '{constraint}' should parse");
        var result = PhpVersionConstraint.Evaluate(target, constraint);
        result.ConstraintIsValid.Should().BeTrue();
        result.TargetIsValid.Should().BeTrue();
        result.IsSatisfied.Should().Be(expected, "target {0} vs '{1}'", target, constraint);
        PhpVersionConstraint.IsSatisfied(target, constraint).Should().Be(expected);
    }
}
