using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// First-class callables of source overload sets (<c>foo(...)</c>, <c>$obj-&gt;m(...)</c>,
/// <c>C::m(...)</c>, an overloaded <c>__invoke</c>) emit the same single implementation as a plain
/// call and run in PHP.
/// </summary>
[Trait("Category", "Overloads")]
public class SourceOverloadCallableRunTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        function pick(int $v): int;
        function pick(string $v): string;
        function pick(int|string $v): int|string {
            return $v;
        }

        final class Repo {
            public function find(int $id): string;
            public function find(string $name): string;
            public function find(int|string $key): string {
                return 'find:' . $key;
            }

            public static function of(int $id): string;
            public static function of(string $name): string;
            public static function of(int|string $key): string {
                return 'of:' . $key;
            }
        }

        final class Doubler {
            public function __invoke(int $v): int;
            public function __invoke(string $v): string;
            public function __invoke(int|string $v): int|string {
                return $v . $v;
            }
        }

        function run(): void {
            $fn = pick(...);
            echo $fn(3), ',', $fn('x'), "\n";

            $repo = new Repo();
            $find = $repo->find(...);
            echo $find(7), ',', $find('seven'), "\n";

            $of = Repo::of(...);
            echo $of(8), ',', $of('eight'), "\n";

            $double = new Doubler();
            echo $double(2), ',', $double('ab'), "\n";
            $viaInvoke = $double->__invoke(...);
            echo $viaInvoke(5), ',', $viaInvoke('c'), "\n";
        }
        """;

    [Fact]
    public void Emit_FirstClassCallables_KeepTheImplementationShape()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        System.Text.RegularExpressions.Regex.Matches(result.Php, @"function pick\(").Count.Should().Be(1, result.Php);
        result.Php.Should().Contain("pick(...)");
        result.Php.Should().Contain("$repo->find(...)");
        result.Php.Should().Contain("Repo::of(...)");
        result.Php.Should().Contain("$double->__invoke(...)");
    }

    [Fact]
    public void Run_FirstClassCallables_ExecuteTheImplementation()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));

        var output = EmittedPhpRunner.Run(result.Files!, @"\Probe\run();");

        output.Should().Be("3,x\nfind:7,find:seven\nof:8,of:eight\n22,abab\n55,cc\n");
    }
}
