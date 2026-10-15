using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Found bug #442: an <c>async</c> closure or arrow function is typed
/// <c>callable(…): Promise&lt;T&gt;</c>, so calls through a variable and generic callable
/// parameters see the Promise.
/// </summary>
[Trait("Category", "Checker")]
public class AsyncClosureTypeTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class User {
            public function __construct(public string $name) {}
        }

        async function find(int $id): User {
            return new User('u' . $id);
        }

        function collect<T extends __Awaited<void|mixed>>(array<Promise<T>> $promises): array<T> {
            return [];
        }

        function mapAsync<TIn, TOut extends __Awaited<void|mixed>>(array<TIn> $items, callable(TIn): Promise<TOut> $fn): array<TOut> {
            return [];
        }
        """;

    [Fact]
    public void AsyncArrowFunction_CallThroughVariable_YieldsPromise()
    {
        var result = SnippetEmit.Compile(Prelude + """

            async function run(): void {
                $fetch = async fn(int $id): User => await find($id);
                Promise<User> $p = $fetch(2);
                User $u = await $fetch(3);
                echo $u->name;
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AsyncArrowFunction_CallThroughVariable_IsNotTheUnwrappedValue()
    {
        var result = SnippetEmit.Compile(Prelude + """

            async function run(): void {
                $fetch = async fn(int $id): User => await find($id);
                User $u = $fetch(2);
            }
            """);

        result.Errors.Should().ContainSingle(e => e.Contains("Promise"));
    }

    [Fact]
    public void AsyncClosure_CallThroughVariable_AwaitedValueIsTyped()
    {
        var result = SnippetEmit.Compile(Prelude + """

            async function run(): void {
                $fetch = async function (int $id): User { return await find($id); };
                echo (await $fetch(2))->name;
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AsyncArrowFunction_PassedToGenericCallable_BindsTypeParameter()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                array<string> $names = mapAsync([1, 2], async fn(int $id): string => (await find($id))->name);
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void PromiseRun_AsyncAndPromiseReturningArrowFunctions_Compile()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                mixed $a = Promise::run(async fn(): User => await find(1));
                mixed $b = Promise::run(fn() => find(2));
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AsyncArrowFunction_MappedThenCollected_InfersElementType()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                array<Promise<User>> $ps = \array_map(async fn(int $id): User => await find($id), [1, 2]);
                array<User> $users = collect($ps);
            }
            """);

        result.Errors.Should().BeEmpty();
    }
}
