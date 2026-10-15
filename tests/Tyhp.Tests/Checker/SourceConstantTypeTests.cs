using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A source <c>const NAME = literal;</c> has no written type; reads take the widened type of
/// the initializer instead of reporting an unresolved type. Found bug #450.
/// </summary>
[Trait("Category", "Checker")]
public class SourceConstantTypeTests
{
    private static void AssertCompiles(string code)
    {
        var result = SnippetEmit.Compile("<?tyhp\nnamespace Probe;\n" + code);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    private static void AssertRejected(string code, string containing)
    {
        var result = SnippetEmit.Compile("<?tyhp\nnamespace Probe;\n" + code);
        result.Errors.Should().Contain(e => e.Contains(containing),
            "errors were: " + string.Join("; ", result.Errors));
    }

    [Fact]
    public void IntConstant_CanBeEchoed()
    {
        AssertCompiles("""
            const LIMIT = 5;

            function t(): void {
                echo LIMIT, "\n";
            }
            """);
    }

    [Fact]
    public void IntConstant_TypesAnUntypedVariable()
    {
        AssertCompiles("""
            const LIMIT = 5;

            function t(): int {
                $n = LIMIT;
                return $n;
            }
            """);
    }

    [Fact]
    public void StringConstant_IsString()
    {
        AssertCompiles("""
            const NAME = 'tyhp';

            function t(): string {
                return NAME;
            }
            """);
    }

    [Fact]
    public void NegativeFloatAndBoolConstants_AreWidened()
    {
        AssertCompiles("""
            const RATIO = -1.5;
            const ENABLED = true;

            function t(): void {
                float $r = RATIO;
                bool $b = ENABLED;
                bool $again = !ENABLED;
            }
            """);
    }

    [Fact]
    public void IntConstant_IsNotAssignableToString()
    {
        AssertRejected("""
            const LIMIT = 5;

            function t(): void {
                string $s = LIMIT;
            }
            """, "`string`");
    }

    [Fact]
    public void ConstantsInOneDeclaration_EachKeepTheirOwnType()
    {
        AssertCompiles("""
            const A = 1, B = 'two';

            function t(): void {
                int $a = A;
                string $b = B;
            }
            """);
    }
}
