using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// The synchronous async entry points: <c>Promise::run(async function (): T { … })</c> is the
/// settled <c>T</c>, and <c>EventLoop::run(Promise&lt;T&gt;)</c> binds <c>T</c> from the promise,
/// including <c>void</c>. Found bugs #12 / #13.
/// </summary>
[Trait("Category", "Checker")]
public class AsyncRunEntryPointTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class Loop {
            public function run<T extends __Awaited<void|mixed> = mixed>(Promise<T> $rootPromise, mixed $token = null): T {
                throw new \Exception('stub');
            }

            public static function runStatic<T extends __Awaited<void|mixed>>(Promise<T> $rootPromise): T {
                throw new \Exception('stub');
            }
        }

        function runFree<T extends __Awaited<void|mixed> = mixed>(Promise<T> $rootPromise): T {
            throw new \Exception('stub');
        }

        function runSettledOnly<T extends __Awaited<mixed>>(Promise<T> $rootPromise): T {
            throw new \Exception('stub');
        }

        async function tick(): void {
        }

        final class Writer {
            public async function write(): void {
                await tick();
            }

            public async function count(): int {
                return 3;
            }

            public async function names(array<int, string> $names): array<int, string> {
                return $names;
            }
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

    // ---- EventLoop::run-shaped signatures with Promise<void> (#13) ----

    [Fact]
    public void InstanceRun_PromiseVoidFromAsyncMethod_Compiles()
    {
        AssertCompiles("""
            function t(Loop $loop, Writer $w): void {
                $loop->run($w->write());
            }
            """);
    }

    [Fact]
    public void InstanceRun_PromiseVoidVariable_Compiles()
    {
        AssertCompiles("""
            function t(Loop $loop): void {
                Promise<void> $p = (async function (): void { await tick(); })();
                $loop->run($p);
            }
            """);
    }

    [Fact]
    public void StaticRun_PromiseVoidWithoutDefault_Compiles()
    {
        AssertCompiles("""
            function t(Writer $w): void {
                Loop::runStatic($w->write());
            }
            """);
    }

    [Fact]
    public void FreeRun_PromiseVoid_Compiles()
    {
        AssertCompiles("""
            function t(Writer $w): void {
                runFree($w->write());
            }
            """);
    }

    [Fact]
    public void InstanceRun_PromiseVoid_ResultIsVoid()
    {
        AssertRejected("""
            function t(Loop $loop, Writer $w): void {
                int $n = $loop->run($w->write());
            }
            """, "`void` to type `int`");
    }

    [Fact]
    public void InstanceRun_PromiseInt_ResultIsInt()
    {
        AssertCompiles("""
            function t(Loop $loop, Writer $w): int {
                int $n = $loop->run($w->count());
                return $n;
            }
            """);
    }

    [Fact]
    public void InstanceRun_PromiseInt_IsNotString()
    {
        AssertRejected("""
            function t(Loop $loop, Writer $w): void {
                string $s = $loop->run($w->count());
            }
            """, "string");
    }

    [Fact]
    public void ConstraintWithoutVoid_StillRejectsPromiseVoid()
    {
        AssertRejected("""
            function t(Writer $w): void {
                runSettledOnly($w->write());
            }
            """, "4010");
    }

    // ---- Promise<void> against a written `void|mixed` / `mixed` type argument ----

    [Fact]
    public void PromiseVoid_ToVoidOrMixedTypeArgument_Compiles()
    {
        AssertCompiles("""
            function take(Promise<void|mixed> $p): void {}

            function t(Writer $w): void {
                take($w->write());
                take($w->count());
            }
            """);
    }

    [Fact]
    public void PromiseVoid_ToMixedTypeArgument_IsRejected()
    {
        AssertRejected("""
            function take(Promise<mixed> $p): void {}

            function t(Writer $w): void {
                take($w->write());
            }
            """, "4010");
    }

    // ---- Promise::run(async function (): T) is the settled T (#12) ----

    [Fact]
    public void PromiseRun_AsyncClosureWithUse_AssignsSettledArray()
    {
        AssertCompiles("""
            function t(Writer $w, array<int, string> $names): array<int, string> {
                array<int, string> $lines = Promise::run(async function () use ($w, $names): array<int, string> {
                    return await $w->names($names);
                });
                return $lines;
            }
            """);
    }

    [Fact]
    public void PromiseRun_AsyncClosure_ReturnedAsSettledInt()
    {
        AssertCompiles("""
            function t(): int {
                return Promise::run(async function (): int {
                    await tick();
                    return 4;
                });
            }
            """);
    }

    [Fact]
    public void PromiseRun_AsyncClosure_IsNotNullable()
    {
        AssertCompiles("""
            function t(): void {
                $x = Promise::run(async function (): int { return 4; });
                int $y = $x + 1;
            }
            """);
    }

    [Fact]
    public void PromiseRun_AsyncVoidClosure_Compiles()
    {
        AssertCompiles("""
            function t(): void {
                Promise::run(async function (): void { await tick(); });
            }
            """);
    }
}
