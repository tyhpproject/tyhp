using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// `mixed` behaves like TypeScript `unknown`: every use that inspects or iterates the value needs
/// a narrowing first, and constants declared `mixed` read as `mixed`.
/// </summary>
public class MixedUseSiteTests
{
    private static void Rejected(string body, MessageCode code, string parameters = "mixed $m")
    {
        var errors = MixedCheckSnippet.Errors($"<?tyhp\nfunction f({parameters}): void {{ {body} }}");
        errors.Select(e => e.Code).Should().Contain(code, MixedCheckSnippet.Format(errors));
    }

    private static void Accepted(string source)
    {
        var errors = MixedCheckSnippet.Errors("<?tyhp\n" + source);
        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Theory]
    [InlineData("$x = $m::class;")]
    [InlineData("$x = $m::FOO;")]
    [InlineData("$x = [...$m];")]
    [InlineData("$x = array(...$m);")]
    [InlineData("$x = $m->p ?? 1;")]
    [InlineData("$x = $m < 3;")]
    [InlineData("$x = $m <= 3;")]
    [InlineData("$x = 3 > $m;")]
    [InlineData("$x = 3 >= $m;")]
    [InlineData("$x = $m <=> 3;")]
    public void UnnarrowedMixedUse_RequiresNarrowing(string body) =>
        Rejected(body, MessageCode.CheckerMixedRequiresNarrowing);

    [Fact]
    public void SpreadInReturnExpression_RequiresNarrowing()
    {
        var errors = MixedCheckSnippet.Errors(
            "<?tyhp\nfunction f(mixed $m): array<int|string, mixed> { return [...$m]; }");
        errors.Select(e => e.Code).Should().ContainSingle(c => c == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Print_OfMixed_IsRejectedLikeEcho() =>
        Rejected("print $m;", MessageCode.CheckerConcatNonStringable);

    [Fact]
    public void Print_OfString_IsAccepted() =>
        Accepted("function f(string $s): void { print $s; }");

    [Fact]
    public void Equality_OnMixed_StaysAllowed() =>
        Accepted("""
            function f(mixed $m): bool { return $m === 3 || $m == 'a' || $m != null; }
            """);

    [Fact]
    public void Relational_OnNarrowedValue_IsAccepted() =>
        Accepted("""
            function f(mixed $m): bool { if (\is_int($m)) { return $m < 3; } return false; }
            """);

    [Fact]
    public void SpreadAndClassConstant_OnNarrowedValues_AreAccepted() =>
        Accepted("""
            function f(mixed $m): array<int|string, mixed> { if (\is_array($m)) { return [...$m]; } return []; }
            function g(mixed $m): string { if ($m instanceof \stdClass) { return $m::class; } return ''; }
            """);

    [Fact]
    public void MixedConstant_ReadsAsMixed_NotItsLiteral()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class C { public const mixed X = 5; }
            function f(): int { return C::X; }
            """);
        errors.Select(e => e.Code).Should().Contain(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void TypedConstant_KeepsItsLiteralType() =>
        Accepted("""
            class C { public const int X = 5; }
            function f(): int { return C::X; }
            """);

    [Fact]
    public void OrOfGuards_NarrowsToUnionOfBranches() =>
        Accepted("""
            function f(mixed $m): int {
                if (\is_int($m) || $m === null) {
                    if ($m === null) { return 0; }
                    return $m;
                }
                return 1;
            }
            function g(mixed $m): int|string {
                if (\is_int($m) || \is_string($m)) { return $m; }
                return 1;
            }
            """);

    [Fact]
    public void NegatedAndOfGuards_NarrowsFallThrough() =>
        Accepted("""
            function f(mixed $m): ?int {
                if (!\is_int($m) && $m !== null) { return 1; }
                return $m;
            }
            """);

    [Fact]
    public void OrOfGuards_DoesNotNarrowWhenOneBranchProvesNothing()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function f(mixed $m, bool $b): int {
                if (\is_int($m) || $b) { return $m; }
                return 1;
            }
            """);
        errors.Select(e => e.Code).Should().Contain(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void BareTypeParameter_BindsNullableArgument()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function id<T>(T $x): T { return $x; }
            function f(?int $a): int { return id($a); }
            """);
        errors.Select(e => e.Code).Should().Contain(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void NullableTypeParameter_BindsNonNullPart() =>
        Accepted("""
            function orZero<T extends int|string>(?T $x, T $default): T { return $x ?? $default; }
            function f(?int $a): int { return orZero($a, 0); }
            """);

    [Fact]
    public void NullableArgument_FailsNonNullableUnionConstraint()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function g<T extends int|string>(T $x): void {}
            function f(?int $a): void { g($a); }
            """);
        errors.Select(e => e.Code).Should().Contain(MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void NullableArgument_SatisfiesNullableConstraint() =>
        Accepted("""
            function g<T extends ?int>(T $x): void {}
            function f(?int $a): void { g($a); g(null); g(1); }
            """);
}
