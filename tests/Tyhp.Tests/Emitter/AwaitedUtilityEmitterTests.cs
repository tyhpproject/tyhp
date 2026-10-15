using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>__Awaited&lt;T&gt;</c> is checker-only: the emitted PHP type is the settled value type
/// (never the <c>\Tyhp\Promise</c> wrapper, never the <c>__Awaited</c> name).
/// </summary>
[Trait("Category", "Emitter")]
public class AwaitedUtilityEmitterTests
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
    public void OpenTypeParameter_ErasesToMixed()
    {
        var php = Emit("""
            function call<T>(callable(): T $f): __Awaited<T> {
                throw new \Exception('stub');
            }
            """);

        php.Should().Contain("function call(callable $f): mixed");
    }

    [Fact]
    public void AwaitedPromise_SpellsTheSettledValueType()
    {
        var php = Emit("""
            function load(callable(): Promise<User> $f): __Awaited<Promise<User>> {
                return Promise::run($f);
            }
            """);

        php.Should().Contain("function load(callable $f): \\Probe\\User");
    }

    [Fact]
    public void PromiseMemberOfUnionInsideAwaited_UnwrapsMemberWise()
    {
        var php = Emit("""
            function take(__Awaited<Promise<User>|User> $user): string {
                return $user->name;
            }
            """);

        php.Should().Contain("function take(\\Probe\\User $user): string");
    }

    [Fact]
    public void Union_UnwrapsMemberWise()
    {
        var php = Emit("""
            function load(callable(): Promise<int>|string $f): __Awaited<Promise<int>|string> {
                return Promise::run($f);
            }
            """);

        php.Should().Contain("function load(callable $f): int|string");
    }

    [Fact]
    public void NullablePromise_KeepsNull()
    {
        var php = Emit("""
            function load(callable(): ?Promise<int> $f): __Awaited<?Promise<int>> {
                return Promise::run($f);
            }
            """);

        php.Should().Contain("function load(callable $f): ?int");
    }

    [Fact]
    public void PromiseOfVoid_SpellsVoid()
    {
        var php = Emit("""
            function load(callable(): Promise<void> $f): __Awaited<Promise<void>> {
                Promise::run($f);
            }
            """);

        php.Should().Contain("function load(callable $f): void");
    }

    [Fact]
    public void ParameterPosition_SpellsSettledType()
    {
        var php = Emit("""
            function take(__Awaited<Promise<User>> $user): string {
                return $user->name;
            }
            """);

        php.Should().Contain("function take(\\Probe\\User $user): string");
    }

    [Fact]
    public void TypeAlias_SpellsSettledType()
    {
        var php = Emit("""
            type Settled = __Awaited<Promise<User>>;

            function name(Settled $user): string {
                return $user->name;
            }
            """);

        php.Should().Contain("function name(\\Probe\\User $user): string");
    }

    [Fact]
    public void RuntimeCheck_OpenTypeParameter_SettlesTheTypeAtRunTime()
    {
        // `T` may itself be a Promise type at run time, so the reified check is the settled type of `T`.
        var php = Emit("""
            function narrow<T>(callable(): T $f): __Awaited<T> {
                mixed $r = $f();
                if ($r is __Awaited<T>) {
                    return $r;
                }
                throw new \Exception('x');
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($r, \\Tyhp\\Type::awaited($__generic_T))");
    }

    [Fact]
    public void RuntimeCheck_ClosedPromiseType_ReifiesTheSettledTypeDirectly()
    {
        var php = Emit("""
            function narrow(mixed $r): bool {
                return $r is __Awaited<Promise<User>>;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::fromClassName(\\Probe\\User::class)");
        php.Should().NotContain("Type::awaited");
    }

    [Fact]
    public void InsidePromiseTypeArgument_KeepsPromiseWrapper()
    {
        var php = Emit("""
            function wrap<T>(callable(): T $f): Promise<__Awaited<T>> {
                return new Promise<__Awaited<T>>($f);
            }
            """);

        php.Should().Contain("): \\Tyhp\\Promise");
    }

    [Fact]
    public void UserGenericCallWithExplicitTypeArgument_EmitsPlainCall()
    {
        var php = Emit("""
            function aw<T>(callable(): T $f): __Awaited<T> {
                throw new \Exception('stub');
            }

            function find(): User {
                return new User('x');
            }

            function run(): void {
                User $user = aw<User>(fn(): User => find());
                User $other = aw(fn(): Promise<User> => Promise::resolve(new User('y')));
            }
            """);

        php.Should().Contain("aw(");
    }
}
