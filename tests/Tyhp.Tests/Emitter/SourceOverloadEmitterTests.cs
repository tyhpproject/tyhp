using System.Text.RegularExpressions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Source overload sets (<c>function f(int $a): int; function f(string $a): string; function f(...) { }</c>)
/// emit only the implementation, and published tyhpdefs list only the declared signatures.
/// </summary>
[Trait("Category", "Overloads")]
public class SourceOverloadEmitterTests
{
    private static string Compile(string body)
    {
        var result = SnippetEmit.Compile("<?tyhp\n" + body);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        return result.Php;
    }

    [Fact]
    public void Function_OverloadSet_EmitsOnlyTheImplementation()
    {
        var php = Compile("""
            function area(int $r): float;
            function area(int $w, int $h): float;
            function area(int $a, int $b = 0): float {
                return $b === 0 ? $a * 3.14 : $a * $b;
            }

            echo area(2);
            echo area(2, 3);
            """);

        Regex.Matches(php, @"function area\(").Count.Should().Be(1, php);
        php.Should().Contain("function area(int $a, int $b = 0): float");
    }

    [Fact]
    public void Method_OverloadSet_EmitsOnlyTheImplementation()
    {
        var php = Compile("""
            class Shape {
                public function scale(int $f): int;
                public function scale(string $f): string;
                public function scale(int|string $f): int|string {
                    return $f;
                }
            }

            function t(Shape $s): void {
                int $a = $s->scale(2);
                string $b = $s->scale('x');
            }
            """);

        Regex.Matches(php, @"function scale\(").Count.Should().Be(1, php);
        php.Should().Contain("$s->scale(2)");
        php.Should().Contain("$s->scale('x')");
    }

    [Fact]
    public void StaticAndInstanceOverloads_EmitOneMemberEach()
    {
        var php = Compile("""
            class Util {
                public static function make(int $a): int;
                public static function make(string $a): string;
                public static function make(int|string $a): int|string {
                    return $a;
                }

                public function wrap(int $a): int;
                public function wrap(string $a): string;
                public function wrap(int|string $a): int|string {
                    return $a;
                }
            }
            """);

        Regex.Matches(php, @"static function make\(").Count.Should().Be(1, php);
        Regex.Matches(php, @"function wrap\(").Count.Should().Be(1, php);
    }

    [Fact]
    public void Interface_OverloadSet_EmitsOneAbstractSignature()
    {
        var php = Compile("""
            interface Repo {
                public function find(int $id): string;
                public function find(string $slug): string;
            }

            class Mem implements Repo {
                public function find(int|string $key): string {
                    return 'x';
                }
            }
            """);

        Regex.Matches(php, @"function find\(").Count.Should().Be(2, php);
    }

    [Fact]
    public void AsyncOverloads_EmitOneAsyncImplementation()
    {
        var php = Compile("""
            async function load(int $id): string;
            async function load(string $slug): string;
            async function load(int|string $key): string {
                return 'x';
            }

            async function caller(): string {
                return await load(1);
            }
            """);

        Regex.Matches(php, @"function load\(").Count.Should().Be(1, php);
    }

    [Fact]
    public void GenericOverloadSet_TwoArgumentCall_SelectsSecondOverload()
    {
        var php = Compile("""
            function pick<T>(T $a): T;
            function pick<T>(T $a, T $b): T;
            function pick<T>(T $a, ?T $b = null): T {
                return $a;
            }

            function t(): void {
                string $y = pick('a', 'b');
            }
            """);

        Regex.Matches(php, @"function pick\(").Count.Should().Be(1, php);
    }

    [Fact]
    public void GenericOverloadSet_KeepsGenericRuntimeSupport()
    {
        var php = Compile("""
            function pick<T>(T $a): T;
            function pick<T>(T $a, T $b): T;
            function pick<T>(T $a, ?T $b = null): T {
                return $a;
            }

            function t(): void {
                int $x = pick(1);
            }
            """);

        Regex.Matches(php, @"function pick\(").Count.Should().Be(1, php);
    }
}
