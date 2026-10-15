using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #169: inside a double-quoted string, <c>{$x->ext()}</c> where <c>ext</c> is an
/// extension method is rewritten to a static call, which PHP cannot interpolate; the string is
/// emitted as a concatenation instead.
/// </summary>
[Trait("Category", "Emitter")]
public class InterpolatedExtensionCallEmitterTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        extension Shout extends string {
            function shout(): string {
                return \strtoupper($this) . '!';
            }
        }

        function greet(string $name): string {
            return "Hello {$name->shout()} and {$name}.";
        }

        function only(string $name): string {
            return "{$name->shout()}";
        }
        """;

    [Fact]
    public void Emit_InterpolatedExtensionCall_BecomesConcatenation()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty();
        result.Php.Should().NotContain("{\\");
        result.Php.Should().Contain("\"Hello \" . (");
    }

    [Fact]
    public void Run_InterpolatedExtensionCall_ProducesTheCallResult()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(
            result.Files!,
            @"echo \Probe\greet('bob'), ""\n"", \Probe\only('amy'), ""\n"";");

        output.Should().Be("Hello BOB! and bob.\nAMY!\n");
    }
}
