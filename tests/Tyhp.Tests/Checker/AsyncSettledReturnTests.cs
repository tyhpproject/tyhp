using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// An <c>async</c> callable's declared return type <c>R</c> is implicitly <c>__Awaited&lt;R&gt;</c>;
/// a call yields <c>Promise&lt;__Awaited&lt;R&gt;&gt;</c>. A <c>T extends __Awaited&lt;…&gt;</c> bound
/// only accepts already-awaited type arguments, and such a parameter is its own awaited type.
/// </summary>
[Trait("Category", "Checker")]
public class AsyncSettledReturnTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class User {
            public function __construct(public string $name) {}
        }

        interface Loader {
            async public function load(): User;
        }

        final class Service {
            async public static function make(): User {
                return new User('s');
            }

            async public function count(): Promise<int> {
                return 4;
            }
        }

        final class Box<TValue extends __Awaited<void|mixed>> {
            public function __construct(public callable(): TValue|Box<TValue> $exec) {}
        }

        async function plainInt(): int {
            return 1;
        }

        async function promiseInt(): Promise<int> {
            return 1;
        }

        async function awaitedInt(): __Awaited<int> {
            return 1;
        }
        """;

    private static void AssertCompiles(string body)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    private static void AssertRejected(string body, string containing)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().Contain(e => e.Contains(containing),
            "errors were: " + string.Join("; ", result.Errors));
    }

    // ---- implicit wrapping, every async form ----

    [Fact]
    public void AsyncFunction_PlainReturn_CallIsPromiseOfSettled()
    {
        AssertCompiles("""
            async function t(): void {
                Promise<int> $p = plainInt();
                int $v = await plainInt();
            }
            """);
    }

    [Fact]
    public void AsyncFunction_ExplicitPromiseReturn_SettlesWithInner()
    {
        AssertCompiles("""
            async function t(): void {
                Promise<int> $p = promiseInt();
                int $v = await promiseInt();
            }
            """);
    }

    [Fact]
    public void AsyncFunction_ExplicitPromiseReturn_IsNotANestedPromise()
    {
        AssertRejected("""
            function t(): void {
                Promise<Promise<int>> $p = promiseInt();
            }
            """, "Promise");
    }

    [Fact]
    public void AsyncFunction_ExplicitAwaitedReturn_DoesNotDoubleWrap()
    {
        AssertCompiles("""
            async function t(): void {
                Promise<int> $p = awaitedInt();
                int $v = await awaitedInt();
            }
            """);
    }

    [Fact]
    public void AsyncStaticMethod_WrapsReturn()
    {
        AssertCompiles("""
            async function t(): void {
                Promise<User> $p = Service::make();
                User $u = await Service::make();
            }
            """);
    }

    [Fact]
    public void AsyncInstanceMethod_ExplicitPromiseReturn_SettlesWithInner()
    {
        AssertCompiles("""
            async function t(Service $s): void {
                Promise<int> $p = $s->count();
                int $n = await $s->count();
            }
            """);
    }

    [Fact]
    public void InterfaceAsyncMethod_WrapsReturn()
    {
        AssertCompiles("""
            async function t(Loader $l): void {
                Promise<User> $p = $l->load();
                User $u = await $l->load();
            }
            """);
    }

    [Fact]
    public void AsyncClosure_WrapsReturn()
    {
        AssertCompiles("""
            async function t(): void {
                callable(): Promise<int> $c = async function (): int { return 1; };
                int $v = await $c();
            }
            """);
    }

    [Fact]
    public void AsyncArrow_ExplicitPromiseReturn_SettlesWithInner()
    {
        AssertCompiles("""
            async function t(): void {
                $c = async fn(): Promise<int> => plainInt();
                int $v = await $c();
            }
            """);
    }

    [Fact]
    public void AsyncArrow_CallIsNotNestedPromise()
    {
        AssertRejected("""
            function t(): void {
                $c = async fn(): Promise<int> => plainInt();
                Promise<Promise<int>> $p = $c();
            }
            """, "Promise");
    }

    // ---- body returns ----

    [Fact]
    public void Body_ReturningSettledValue_Compiles()
    {
        AssertCompiles("""
            async function f(): Promise<int> {
                return 2;
            }
            """);
    }

    [Fact]
    public void Body_ReturningPromiseThatSettlesWithDeclared_Compiles()
    {
        AssertCompiles("""
            async function f(): int {
                return plainInt();
            }

            async function g(): Promise<int> {
                return promiseInt();
            }
            """);
    }

    [Fact]
    public void Body_ReturningPromiseOfWrongType_IsRejected()
    {
        AssertRejected("""
            async function f(): int {
                return Promise::resolve('x');
            }
            """, "4009");
    }

    [Fact]
    public void Body_ReturningWrongValue_IsRejected()
    {
        AssertRejected("""
            async function f(): Promise<int> {
                return 'x';
            }
            """, "4009");
    }

    [Fact]
    public void Body_SynchronousClosureInsideAsync_KeepsItsOwnReturnType()
    {
        // The nested plain closure is not an async return scope: it must return a real Promise.
        AssertRejected("""
            async function f(): int {
                callable(): Promise<int> $c = function (): Promise<int> {
                    return 5;
                };
                return await $c();
            }
            """, "4009");
    }

    // ---- constraint validation ----

    [Fact]
    public void AwaitedBound_AcceptsSettledArguments()
    {
        AssertCompiles("""
            function t(callable(): int $a, callable(): string|int $b, callable(): mixed $c): void {
                Box<int> $x = new Box<int>($a);
                Box<string|int> $y = new Box<string|int>($b);
                Box<mixed> $z = new Box<mixed>($c);
            }
            """);
    }

    [Fact]
    public void AwaitedBound_RejectsPromiseArgument()
    {
        AssertRejected("""
            function t(Box<Promise<int>> $b): void {}
            """, "4373");
    }

    [Fact]
    public void AwaitedBound_RejectsUnionContainingPromise()
    {
        AssertRejected("""
            function t(Box<int|Promise<string>> $b): void {}
            """, "4373");
    }

    [Fact]
    public void AwaitedBound_RejectsNestedPromiseOfPromise()
    {
        AssertRejected("""
            function t(Promise<Promise<int>> $p): void {}
            """, "4373");
    }

    [Fact]
    public void AwaitedBound_RejectsUnconstrainedTypeParameter()
    {
        AssertRejected("""
            function t<T>(Box<T> $b): void {}
            """, "4373");
    }

    [Fact]
    public void AwaitedBound_RejectsMixedBoundTypeParameter()
    {
        AssertRejected("""
            function t<T extends void|mixed>(Promise<T> $p): void {}
            """, "4373");
    }

    [Fact]
    public void AwaitedBound_AcceptsAwaitedBoundTypeParameter()
    {
        AssertCompiles("""
            function t<T extends __Awaited<void|mixed>>(Box<T> $b, Promise<T> $p): void {}
            """);
    }

    [Fact]
    public void AwaitedBound_AcceptsTypeParameterWithSettledBound()
    {
        AssertCompiles("""
            function t<T extends int|string>(Promise<T> $p): void {}
            """);
    }

    [Fact]
    public void AwaitedBound_AcceptsDeferredAwaitedOfOpenParameter()
    {
        AssertCompiles("""
            function t<T extends void|mixed>(Promise<__Awaited<T>> $p): void {}
            """);
    }

    // ---- normalization inside generic code ----

    [Fact]
    public void AwaitedBoundParameter_AwaitedOfItself_IsItself()
    {
        AssertCompiles("""
            function toAwaited<T extends __Awaited<void|mixed>>(T $v): __Awaited<T> {
                return $v;
            }

            function fromAwaited<T extends __Awaited<void|mixed>>(__Awaited<T> $v): T {
                return $v;
            }
            """);
    }

    [Fact]
    public void AwaitedBoundParameter_AwaitingPromiseOfIt_YieldsIt()
    {
        AssertCompiles("""
            async function f<T extends __Awaited<void|mixed>>(Promise<T> $p): T {
                T $v = await $p;
                return $v;
            }
            """);
    }

    [Fact]
    public void AwaitedBoundParameter_PromiseOfItself_IsValid()
    {
        AssertCompiles("""
            function f<T extends __Awaited<void|mixed>>(Promise<T> $p): Promise<T> {
                return $p;
            }
            """);
    }

    [Fact]
    public void MixedStillRejected_ForDeferredAwaited()
    {
        AssertRejected("""
            function f<T extends void|mixed>(mixed $m): __Awaited<T> {
                return $m;
            }
            """, "mixed");
    }

    [Fact]
    public void OpenParameter_NotAssignableToItsDeferredAwaited()
    {
        AssertRejected("""
            function f<T extends void|mixed>(T $v): __Awaited<T> {
                return $v;
            }
            """, "T");
    }

    // ---- executor shape ----

    [Fact]
    public void ExecutorShape_PlainCallableOfTypeParameter_IsAccepted()
    {
        AssertCompiles("""
            function wrap<T extends void|mixed>(callable(): T $fn): Promise<__Awaited<T>> {
                return new Promise<__Awaited<T>>($fn);
            }
            """);
    }

    [Fact]
    public void ExecutorShape_IsNotAGeneralAwaitedLeniency()
    {
        AssertRejected("""
            function f<T extends void|mixed>(callable(): T $fn): callable(): __Awaited<T> {
                return $fn;
            }
            """, "4009");
    }
}
