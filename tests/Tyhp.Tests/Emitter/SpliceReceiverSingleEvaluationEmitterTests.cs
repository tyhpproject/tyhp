using System.Text.RegularExpressions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #447: a spliced extension member must evaluate a non-trivial receiver or argument
/// exactly once, including when later reads sit behind <c>&amp;&amp;</c>, <c>||</c> or a ternary
/// branch.
/// </summary>
[Trait("Category", "Emitter")]
public class SpliceReceiverSingleEvaluationEmitterTests
{
    private const string Header = """
        <?tyhp
        namespace Demo4;

        extension LocalInt extends int {
            fn between(int $min, int $max): bool => $this >= $min && $this <= $max;
            fn rng(int $min, int $max): bool => $this >= $min && $this <= $max && $min < $max;
            fn nest(int $lo): int => ($this > $lo || $this < 0) ? $this : 0;
            fn mixThis(int $x): int => ($this > 0 && $x > 0 ? 1 : 0) + $this;
            fn mix(int $x): int => ($this > 0 && $x > 0 ? 1 : 0) + $x;
            fn plain(int $x): int => $this + $x;
        }
        use extension LocalInt;

        function lo(): int { return 3; }
        function hi(): int { return 4; }

        """;

    private static string Emit(string body)
    {
        var result = SnippetEmit.Compile(Header + body);
        result.Errors.Should().BeEmpty();
        return result.Php;
    }

    private static int Count(string php, string needle) =>
        Regex.Matches(php, @"(?<!function )" + Regex.Escape(needle)).Count;

    [Fact]
    public void Between_CallReceiver_IsEvaluatedOnceAndBoundAtFirstUse()
    {
        var php = Emit("bool $a = lo()->between(0, 5);");

        Count(php, "lo()").Should().Be(1);
        php.Should().Contain("($__tyhpInlineTemp1 = lo()) >= 0");
        php.Should().Contain("$__tyhpInlineTemp1 <= 5");
    }

    [Fact]
    public void Between_NewReceiver_IsEvaluatedOnce()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Demo4;

            class Box { public function __construct(public int $v) {} }

            extension BoxExt extends Box {
                fn inRange(int $min, int $max): bool => $this->v >= $min && $this->v <= $max;
            }
            use extension BoxExt;

            bool $a = (new Box(3))->inRange(0, 5);
            """);

        result.Errors.Should().BeEmpty();
        Regex.Matches(result.Php, @"new \\?(Demo4\\)?Box\(3\)").Count.Should().Be(1);
    }

    [Fact]
    public void Range_ReceiverUsedOnlyBehindShortCircuit_IsEvaluatedOnce()
    {
        var php = Emit("bool $a = lo()->rng(1, 5);");

        Count(php, "lo()").Should().Be(1);
        php.Should().Contain("($__tyhpInlineTemp1 = lo()) >= 1");
    }

    [Fact]
    public void Ternary_ReceiverReadInConditionAndBranches_IsEvaluatedOnce()
    {
        var php = Emit("int $a = lo()->nest(2);");

        Count(php, "lo()").Should().Be(1);
        php.Should().Contain("($__tyhpInlineTemp1 = lo()) > 2");
    }

    [Fact]
    public void Receiver_FirstUseAlwaysRuns_BindsThere()
    {
        var php = Emit("int $a = lo()->mixThis(1);");

        Count(php, "lo()").Should().Be(1);
        php.Should().Contain("($__tyhpInlineTemp1 = lo()) > 0");
    }

    [Fact]
    public void ErasedMember_ArgumentReadBehindShortCircuitBeforeFirstAlwaysRunsRead_IsRejected()
    {
        // The read of `$x` behind `&&` precedes the unconditional one, so a temp bound at the
        // later read would be read before it is assigned.
        var result = SnippetEmit.Compile(Header + "int $a = lo()->mix(hi());");

        result.Errors.Should().ContainSingle(e => e.Contains("mix") && e.Contains("spliced"));
    }

    [Fact]
    public void ErasedMember_ArgumentReadBehindShortCircuitBeforeFirstAlwaysRunsRead_SimpleArgumentIsStillSpliced()
    {
        var php = Emit("int $y = 2; int $a = lo()->mix($y);");

        php.Should().NotContain("LocalInt::mix");
        Count(php, "lo()").Should().Be(1);
    }

    [Fact]
    public void PhpBackedMember_ArgumentReadBehindShortCircuitBeforeFirstAlwaysRunsRead_KeepsTheRealCall()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Demo4;

            extension BackedInt extends int {
                function mix(int $x): int {
                    return ($this > 0 && $x > 0 ? 1 : 0) + $x;
                }
            }
            use extension BackedInt;

            function lo(): int { return 3; }
            function hi(): int { return 4; }

            int $a = lo()->mix(hi());
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("BackedInt::mix(lo(), hi())");
        result.Php.Should().NotContain("$__tyhpInlineTemp");
    }

    [Fact]
    public void SingleUse_Receiver_IsInlinedWithoutTemp()
    {
        var php = Emit("int $a = lo()->plain(hi());");

        php.Should().NotContain("$__tyhpInlineTemp");
        Count(php, "lo()").Should().Be(1);
        Count(php, "hi()").Should().Be(1);
    }
}
