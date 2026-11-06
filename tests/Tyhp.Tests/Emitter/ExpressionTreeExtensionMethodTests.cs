using FluentAssertions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Extension calls inside an expression-tree lambda stay method calls. The backing closure
/// is still spliced so the tree can be compiled and run.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionTreeExtensionMethodTests
{
    private const string Prelude = """
        <?tyhp
        extension ProbeStrings extends string {
            fn startsWith(string $prefix): bool => $this !== $prefix;
        }
        class Post {
            public string $title = '';
            public ?string $name = null;
        }
        function take(\Tyhp\Expression<callable(Post): bool> $e): void {}
        function takeNull(\Tyhp\Expression<callable(Post): ?bool> $e): void {}
        """;

    [Fact]
    public void ExtensionCall_EmitsMethodCallAndSplicesBackingClosure()
    {
        var result = Compile("function demo(): void { take(fn ($p) => $p->title->startsWith('x')); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("->title->call('startsWith', 'x')");
        result.Php.Should().Contain("!== 'x'");
        result.Php.Should().NotContain("->startsWith(");
    }

    [Fact]
    public void ExtensionCall_OutsideATree_StillSplices()
    {
        var result = Compile("function demo(Post $p): bool { return $p->title->startsWith('x'); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("!== 'x'");
        result.Php.Should().NotContain("->call('startsWith'");
    }

    [Fact]
    public void NullSafeExtensionCall_EmitsNullSafeMethodCall()
    {
        var result = Compile("function demo(): void { takeNull(fn ($p) => $p->name?->startsWith('x')); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("->name->nullSafe()->call('startsWith', 'x')");
    }

    private static SnippetEmit.Result Compile(string function) => SnippetEmit.Compile(Prelude + "\n" + function);
}
