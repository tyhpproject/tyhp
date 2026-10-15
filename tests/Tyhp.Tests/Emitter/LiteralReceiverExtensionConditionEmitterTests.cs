using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #448: a language constant (<c>true</c>, <c>false</c>) used as an extension receiver
/// has its own type, so the call result is <c>bool</c> in a condition just as in an assignment.
/// </summary>
[Trait("Category", "Emitter")]
public class LiteralReceiverExtensionConditionEmitterTests
{
    private const string Header = """
        <?tyhp
        namespace Demo5;

        extension BoolExt extends bool {
            fn not(): bool => !$this;
        }
        use extension BoolExt;

        """;

    [Theory]
    [InlineData("echo true->not() ? \"yes\" : \"no\";", "echo !true ? 'yes' : 'no';")]
    [InlineData("echo false->not() ? \"yes\" : \"no\";", "echo !false ? 'yes' : 'no';")]
    [InlineData("if (true->not()) { echo 'x'; }", "if (!true)")]
    [InlineData("while (false->not()) { break; }", "while (!false)")]
    public void LiteralReceiver_InConditionPosition_InfersBool(string body, string expectedFragment)
    {
        var result = SnippetEmit.Compile(Header + body);

        result.Errors.Should().BeEmpty();
        result.Php.Replace('"', '\'').Should().Contain(expectedFragment);
    }

    [Fact]
    public void LiteralReceiver_ResultIsBool_NotUnresolved()
    {
        var result = SnippetEmit.Compile(Header + "int $n = true->not();");

        result.Errors.Should().NotBeEmpty("the extension call yields `bool`, which is not an `int`");
    }
}
