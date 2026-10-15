using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #380: reading an omitted optional (nullable, no default) struct property must yield
/// <c>null</c> without a PHP "Undefined array key" warning.
/// </summary>
[Trait("Category", "Emitter")]
public class StructOptionalPropertyReadEmitterTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        type Opt = struct { string $a; ?string $b; string $c = 'dflt'; ?string $d = null; };

        function run(): void {
            Opt $t = new Opt() with [a => 'x'];
            ?string $b = $t->b;
            \var_dump($b);
            \var_dump($t->d);
            \var_dump($t->c);

            \var_dump(isset($t->b));
            \var_dump($t->b ?? 'fallback');
            \var_dump(empty($t->b));

            $t->b = 'set';
            \var_dump($t->b);
            unset($t->b);
            \var_dump($t->b);
            $t->b ??= 'again';
            \var_dump($t->b);
        }
        """;

    [Fact]
    public void Emit_OptionalStructRead_IsGuardedAndWritesStayBare()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("= ($t['b'] ?? null);");
        result.Php.Should().Contain("$t['b'] = 'set';");
        result.Php.Should().Contain("isset($t['b'])");
        result.Php.Should().Contain("unset($t['b'])");
        result.Php.Should().Contain("$t['b'] ??= 'again'");
        result.Php.Should().NotContain("$t['a'] ?? null");
        result.Php.Should().NotContain("$t['c'] ?? null");
        result.Php.Should().NotContain("$t['d'] ?? null");
    }

    [Fact]
    public void Run_OptionalStructRead_RaisesNoWarning()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(
            result.Files!,
            """
            \set_error_handler(static function (int $no, string $message): bool {
                throw new \ErrorException($message);
            });
            \Probe\run();
            """);

        output.Should().Be(
            "NULL\nNULL\nstring(4) \"dflt\"\nbool(false)\nstring(8) \"fallback\"\nbool(true)\nstring(3) \"set\"\nNULL\nstring(5) \"again\"\n");
    }
}
