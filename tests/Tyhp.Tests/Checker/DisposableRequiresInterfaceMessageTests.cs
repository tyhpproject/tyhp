using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Found bug #449: TYHP4030 fires for <c>:=</c>, <c>using</c> and <c>using await</c>, so its text
/// names every construct rather than only <c>:=</c>.
/// </summary>
[Trait("Category", "Checker")]
public class DisposableRequiresInterfaceMessageTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        class Plain {}

        final class Handle implements \Tyhp\Contracts\IsDisposable {
            public function dispose(): void {}
        }

        """;

    [Theory]
    [InlineData("$x := new Plain();")]
    [InlineData("using ($x = new Plain()) { }")]
    public void NonDisposableResource_MessageNamesEveryConstruct(string statement)
    {
        var result = SnippetEmit.Compile(Prelude + $$"""
            function run(): void {
                {{statement}}
            }
            """);

        var error = result.Errors.Should().ContainSingle(e => e.Contains("IsDisposable")).Subject;
        error.Should().Contain("`:=`").And.Contain("`using`").And.Contain("`using await`");
    }
}
