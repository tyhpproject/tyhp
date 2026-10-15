using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Found bugs #435 and #436: TYHP4322 names unsupported expression-tree constructs readably, and
/// rejects the constructs a tree cannot describe (first-class callables, shell commands).
/// </summary>
[Trait("Category", "Checker")]
public class ExpressionTreeUnsupportedKindTests
{
    private const string Prelude = """
        <?tyhp
        class User {
            public int $age = 0;
            public ?string $nick = null;
            public function displayName(): string { return ''; }
        }
        function take(\Tyhp\Expression<callable(User): mixed> $pred): void {}
        """;

    [Theory]
    [InlineData("[1, 2]", "array literal")]
    [InlineData("[...[1]]", "array literal")]
    [InlineData("isset($u->nick)", "isset")]
    [InlineData("empty($u->nick)", "empty")]
    [InlineData("nameof(User::$age)", "nameof")]
    public void UnsupportedBody_ReportsReadableKind(string body, string kind)
    {
        var errors = Errors($"function demo(): void {{ take(fn ($u) => {body}); }}");

        errors.Should().ContainSingle(e => e.StartsWith("4322"));
        errors.Single(e => e.StartsWith("4322")).Should().Contain($"`{kind}`");
    }

    [Fact]
    public void FirstClassCallableBody_Reports4322()
    {
        var errors = Errors("function demo(): void { take(fn ($u) => $u->displayName(...)); }");

        errors.Should().ContainSingle(e => e.StartsWith("4322") && e.Contains("`first-class callable`"));
    }

    [Fact]
    public void ShellCommandBody_Reports4322()
    {
        var errors = Errors("function demo(): void { take(fn ($u) => `ls`); }");

        errors.Should().ContainSingle(e => e.StartsWith("4322") && e.Contains("`shell command`"));
    }

    [Fact]
    public void NullableInstanceofBody_EmitsNullableTargetType()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function demo(): void { take(fn ($u) => $u->nick is ?string); }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("'nick', '?string'), '?string', 'bool')");
    }

    [Fact]
    public void NullableInstanceofClassBody_EmitsNullableClassTarget()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function demo(): void { take(fn ($u) => $u is ?User); }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("'?' . \\User::class");
    }

    private static List<string> Errors(string function)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + function);
        return result.Errors.ToList();
    }
}
