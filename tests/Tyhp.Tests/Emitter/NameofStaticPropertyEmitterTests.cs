using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #431: <c>nameof(Class::$property)</c> yields the bare property name and is usable
/// wherever a <c>string</c> is (echo, interpolation).
/// </summary>
[Trait("Category", "Emitter")]
public class NameofStaticPropertyEmitterTests
{
    private const string Plain = """
        <?tyhp
        final class Plain {
            public static int $counter = 0;
            public function __construct(public string $name = 'x') {}
        }
        """;

    [Theory]
    [InlineData("Plain::$counter", "'counter'")]
    [InlineData("Plain::$name", "'name'")]
    public void Nameof_StaticStyleProperty_OmitsDollar(string operand, string expected)
    {
        var result = SnippetEmit.Compile($$"""
            {{Plain}}
            string $n = nameof({{operand}});
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$n = " + expected + ";");
    }

    [Fact]
    public void Echo_NameofProperty_IsAccepted()
    {
        var result = SnippetEmit.Compile($$"""
            {{Plain}}
            echo nameof(Plain::$name), "\n";
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("echo 'name'");
    }
}
