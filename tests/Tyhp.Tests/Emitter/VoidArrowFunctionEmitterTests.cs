using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #440: PHP rejects an arrow function declared <c>: void</c>, so Tyhp emits a closure
/// with a block body (explicit <c>use</c> list) for it.
/// </summary>
[Trait("Category", "Emitter")]
public class VoidArrowFunctionEmitterTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        function cleanup(string $message): void {
            echo $message, "\n";
        }

        function apply(callable(): void $fn): void {
            $fn();
        }

        function run(): void {
            string $tag = 'tag';
            string $unused = 'never captured';
            apply(fn(): void => cleanup($tag . '-done'));
            callable(string): void $show = static fn(string $m): void => cleanup($m);
            $show('shown');
        }
        """;

    [Fact]
    public void Emit_VoidArrowFunction_BecomesClosureWithExplicitUse()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("function () use ($tag): void {");
        result.Php.Should().Contain("static function (string $m): void {");
        result.Php.Should().NotContain("fn(): void");
        result.Php.Should().NotContain("): void =>");
        result.Php.Should().NotContain("use ($unused)");
    }

    [Fact]
    public void Run_VoidArrowFunction_LoadsAndCapturesByValue()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(result.Files!, @"\Probe\run();");

        output.Should().Be("tag-done\nshown\n");
    }
}
