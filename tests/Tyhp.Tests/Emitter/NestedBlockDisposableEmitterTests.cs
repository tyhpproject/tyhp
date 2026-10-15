using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #443: a <c>:=</c> declared in a nested block is disposed when that block ends, not
/// when the enclosing function returns.
/// </summary>
[Trait("Category", "Emitter")]
public class NestedBlockDisposableEmitterTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        final class Res implements \Tyhp\Contracts\IsDisposable {
            public function __construct(private string $name) {}
            public function dispose(): void { echo "dispose ", $this->name, "\n"; }
        }

        function run(bool $c): void {
            $outer := new Res('outer');
            if ($c) {
                $inner := new Res('inner');
                echo "in block\n";
            }
            echo "after\n";
        }

        function loop(): void {
            foreach ([1, 2] as $i) {
                $item := new Res('item' . $i);
                echo "body ", $i, "\n";
            }
            echo "done\n";
        }
        """;

    [Fact]
    public void Run_NestedBlockLocal_IsDisposedAtBlockEnd()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(result.Files!, @"\Probe\run(true);");

        output.Should().Be("in block\ndispose inner\nafter\ndispose outer\n");
    }

    [Fact]
    public void Run_LoopBodyLocal_IsDisposedEachIteration()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(result.Files!, @"\Probe\loop();");

        output.Should().Be("body 1\ndispose item1\nbody 2\ndispose item2\ndone\n");
    }
}
