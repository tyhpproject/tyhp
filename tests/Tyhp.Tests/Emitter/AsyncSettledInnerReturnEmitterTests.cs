using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// An <c>async</c> callable's declared return type is the settled value type: the outer PHP
/// signature stays <c>\Tyhp\Promise</c> and the inner <c>Promise::_async</c> closure carries the
/// settled type (never the <c>\Tyhp\Promise</c> wrapper, never <c>__Awaited</c>). A body that
/// returns a Promise leaves the inner closure untyped so the runtime adopts it.
/// </summary>
[Trait("Category", "Emitter")]
public class AsyncSettledInnerReturnEmitterTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class User {
            public function __construct(public string $name) {}
        }

        """;

    private static string Emit(string body)
    {
        var result = SnippetEmit.Compile(Prelude + body);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().NotContain("__Awaited");
        return result.Php;
    }

    [Fact]
    public void AsyncFunction_PlainReturn_InnerClosureCarriesTheDeclaredType()
    {
        var php = Emit("""
            async function load(): int {
                return 1;
            }
            """);

        php.Should().Contain("function load(): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function (): int {");
    }

    [Fact]
    public void AsyncFunction_ExplicitPromiseReturn_InnerClosureCarriesTheSettledType()
    {
        var php = Emit("""
            async function load(): Promise<int> {
                return 1;
            }
            """);

        php.Should().Contain("function load(): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function (): int {");
        php.Should().NotContain("function (): \\Tyhp\\Promise {");
    }

    [Fact]
    public void AsyncFunction_ExplicitAwaitedReturn_DoesNotDoubleWrap()
    {
        var php = Emit("""
            async function load(): __Awaited<Promise<User>> {
                return new User('a');
            }
            """);

        php.Should().Contain("\\Tyhp\\Promise::_async(function (): \\Probe\\User {");
    }

    [Fact]
    public void AsyncFunction_ReturningAPromise_LeavesTheInnerClosureUntyped()
    {
        var php = Emit("""
            async function load(): int {
                return Promise::resolve<int>(1);
            }
            """);

        php.Should().Contain("\\Tyhp\\Promise::_async(function () {");
    }

    [Fact]
    public void AsyncMethod_InnerClosureCarriesTheDeclaredObjectType()
    {
        var php = Emit("""
            class Repo {
                async public function find(): User {
                    return new User('a');
                }
            }
            """);

        php.Should().Contain("public function find(): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function (): \\Probe\\User {");
    }

    [Fact]
    public void AsyncStaticMethod_ExplicitPromiseReturn_InnerClosureCarriesTheSettledType()
    {
        var php = Emit("""
            class Repo {
                async public static function count(): Promise<int> {
                    return 3;
                }
            }
            """);

        php.Should().Contain("public static function count(): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function (): int {");
    }

    [Fact]
    public void AsyncArrow_InnerArrowCarriesTheDeclaredType()
    {
        var php = Emit("""
            function demo(): void {
                $f = async fn(): int => 5;
            }
            """);

        php.Should().Contain("fn(): \\Tyhp\\Promise => \\Tyhp\\Promise::_async(fn(): int => 5)");
    }

    [Fact]
    public void AsyncClosure_InnerClosureCarriesTheDeclaredType()
    {
        var php = Emit("""
            function demo(): void {
                $f = async function (): string {
                    return 'x';
                };
            }
            """);

        php.Should().Contain("\\Tyhp\\Promise::_async(function (): string {");
    }
}
