using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Found bug #441: <c>:=</c>, <c>using</c> and <c>using await</c> reject a value whose class does
/// not implement <c>IsDisposable</c> / <c>AsyncIsDisposable</c>, and <c>using await</c> needs an
/// async context.
/// </summary>
[Trait("Category", "Checker")]
public class DisposableContractTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        class Plain {}

        final class Handle implements \Tyhp\Contracts\IsDisposable {
            public function dispose(): void {}
        }
        """;

    [Fact]
    public void UsingEqual_NonDisposableClass_IsRejected()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                $x := new Plain();
            }
            """);

        result.Errors.Should().ContainSingle(e => e.Contains("IsDisposable"));
    }

    [Fact]
    public void UsingBlock_NonDisposableClass_IsRejected()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                using ($y = new Plain()) {
                }
            }
            """);

        result.Errors.Should().ContainSingle(e => e.Contains("IsDisposable"));
    }

    [Fact]
    public void UsingEqualAndUsingBlock_DisposableClass_AreAccepted()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                $a := new Handle();
                using ($b = new Handle()) {
                }
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UsingAwait_OutsideAsyncFunction_IsRejected()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                using await ($z = new Handle()) {
                }
            }
            """);

        result.Errors.Should().ContainSingle(e => e.Contains("async"));
    }
}
