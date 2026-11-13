using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Disposal must not initialize an uninitialized lazy object. The emitted guard calls
/// <c>ReflectionClass::isUninitializedLazyObject</c> itself, behind <c>\PHP_VERSION_ID</c> when the
/// build floor is older than 8.4, so it needs no runtime package and still skips a ghost when a
/// build for an older PHP runs on 8.4 or newer.
/// </summary>
[Trait("Category", "Emitter")]
public class LazyDisposeEmitterTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        final class Res implements \Tyhp\Contracts\IsDisposable {
            public string $name;
            public function __construct(string $name) { $this->name = $name; }
            public function dispose(): void { echo "dispose ", $this->name, "\n"; }
        }

        function useOne(): void {
            using ($h = new Res('handle')) {
                echo "body\n";
            }
        }

        function useGiven(Res $r): void {
            using ($h = $r) {
                echo "body\n";
            }
        }

        function useNested(bool $c): void {
            if ($c) {
                $inner := new Res('inner');
                echo "in block\n";
            }
        }

        function useNestedGiven(Res $r): void {
            if (true) {
                $inner := $r;
                echo "in block\n";
            }
        }

        async function useAwait(): void {
            using await ($c = new Res('async')) {
                echo "awaited\n";
            }
        }
        """;

    private const string GhostPrelude = """
        if (\PHP_VERSION_ID < 80400) {
            echo "no lazy objects\n";
            return;
        }
        $make = static fn (): \Probe\Res => (new \ReflectionClass(\Probe\Res::class))->newLazyGhost(
            static function (\Probe\Res $object): void {
                echo "initialized\n";
                $object->__construct('ghost');
            });
        $state = static fn (object $o): string => (new \ReflectionClass($o))->isUninitializedLazyObject($o)
            ? "uninitialized\n"
            : "initialized\n";
        """;

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Using_BelowPhp84_GuardsReflectionCallBehindVersionId(string phpVersion)
    {
        var result = SnippetEmit.Compile(Probe, phpVersion);
        result.Errors.Should().BeEmpty();

        result.Php.Should().Contain(
            "if ($h instanceof \\Tyhp\\Contracts\\IsDisposable && "
            + "!(\\PHP_VERSION_ID >= 80400 && (new \\ReflectionClass($h))->isUninitializedLazyObject($h)))");
        result.Php.Should().Contain(
            "if ($inner instanceof \\Tyhp\\Contracts\\IsDisposable && "
            + "!(\\PHP_VERSION_ID >= 80400 && (new \\ReflectionClass($inner))->isUninitializedLazyObject($inner)))");
        result.Php.Should().Contain(
            "if ($c instanceof \\Tyhp\\Contracts\\AsyncIsDisposable && "
            + "!(\\PHP_VERSION_ID >= 80400 && (new \\ReflectionClass($c))->isUninitializedLazyObject($c)))");
        result.Php.Should().Contain(
            "elseif ($c instanceof \\Tyhp\\Contracts\\IsDisposable && "
            + "!(\\PHP_VERSION_ID >= 80400 && (new \\ReflectionClass($c))->isUninitializedLazyObject($c)))");
        result.Php.Should().NotContain("\\Tyhp\\Lazy");
    }

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Using_AtOrAbovePhp84_CallsReflectionWithoutVersionId(string phpVersion)
    {
        var result = SnippetEmit.Compile(Probe, phpVersion);
        result.Errors.Should().BeEmpty();

        result.Php.Should().Contain(
            "if ($h instanceof \\Tyhp\\Contracts\\IsDisposable && "
            + "!(new \\ReflectionClass($h))->isUninitializedLazyObject($h))");
        result.Php.Should().Contain(
            "if ($c instanceof \\Tyhp\\Contracts\\AsyncIsDisposable && "
            + "!(new \\ReflectionClass($c))->isUninitializedLazyObject($c))");
        result.Php.Should().Contain(
            "elseif ($c instanceof \\Tyhp\\Contracts\\IsDisposable && "
            + "!(new \\ReflectionClass($c))->isUninitializedLazyObject($c))");
        result.Php.Should().NotContain("PHP_VERSION_ID");
        result.Php.Should().NotContain("\\Tyhp\\Lazy");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Using_OrdinaryObject_StillDisposes(string phpVersion)
    {
        var result = SnippetEmit.Compile(Probe, phpVersion);
        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$h->dispose()");
        result.Php.Should().Contain("$inner->dispose()");
        result.Php.Should().Contain("$c->dispose()");

        var output = EmittedPhpRunner.Run(result.Files!, """
            \Probe\useOne();
            \Probe\useNested(true);
            """);

        output.Should().Be("body\ndispose handle\nin block\ndispose inner\n");
    }

    // The 8.2 build is the one that runs on a newer PHP: the case a floor-gated check would miss.
    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    [InlineData("8.4")]
    public void Using_UninitializedGhost_IsNotInitializedOrDisposed(string phpVersion)
    {
        var result = SnippetEmit.Compile(Probe, phpVersion);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(result.Files!, GhostPrelude + """

            $ghost = $make();
            \Probe\useGiven($ghost);
            echo $state($ghost);
            $nested = $make();
            \Probe\useNestedGiven($nested);
            echo $state($nested);
            """);

        output.Should().BeOneOf(
            "no lazy objects\n",
            "body\nuninitialized\nin block\nuninitialized\n");
    }

    [Fact]
    public void Using_InitializedGhost_StillDisposes()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.Run(result.Files!, GhostPrelude + """

            $ghost = $make();
            $ghost->name;
            \Probe\useGiven($ghost);
            $nested = $make();
            $nested->name;
            \Probe\useNestedGiven($nested);
            """);

        output.Should().BeOneOf(
            "no lazy objects\n",
            "initialized\nbody\ndispose ghost\ninitialized\nin block\ndispose ghost\n");
    }

    // The emitted-text tests above cover the AsyncIsDisposable arm. Here a ghost with only the sync
    // contract falls through that arm to the sync fallback, which must skip it too.
    [Fact]
    public void UsingAwait_UninitializedGhost_SyncFallbackSkips()
    {
        var result = SnippetEmit.Compile(AwaitProbe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.RunAgainstRuntimeDist(result.Files!, GhostPrelude + """

            $sync = $make();
            \Tyhp\Promise::run(static function () use ($sync): void {
                \Tyhp\Promise::_await(\Probe\awaitSync($sync));
            });
            echo $state($sync);

            $sync = $make();
            $sync->name;
            \Tyhp\Promise::run(static function () use ($sync): void {
                \Tyhp\Promise::_await(\Probe\awaitSync($sync));
            });
            """, "core", "async");
        if (output is null)
        {
            return;
        }

        output.Should().BeOneOf(
            "no lazy objects\n",
            "awaited\nuninitialized\ninitialized\nawaited\ndispose ghost\n");
    }

    [Fact]
    public void FunctionScopeUsingEquals_UninitializedGhost_IsNotInitializedOrDisposed()
    {
        var result = SnippetEmit.Compile(ScopeProbe);
        result.Errors.Should().BeEmpty();

        var output = EmittedPhpRunner.RunAgainstRuntimeDist(result.Files!, GhostPrelude + """

            $ghost = $make();
            \Probe\scoped($ghost);
            echo $state($ghost);

            $used = $make();
            $used->name;
            \Probe\scoped($used);
            """, "core", "async");
        if (output is null)
        {
            return;
        }

        output.Should().BeOneOf(
            "no lazy objects\n",
            "scoped\nuninitialized\ninitialized\nscoped\ndispose ghost\n");
    }

    private const string AwaitProbe = """
        <?tyhp
        namespace Probe;

        class Res implements \Tyhp\Contracts\IsDisposable {
            public string $name;
            public function __construct(string $name) { $this->name = $name; }
            public function dispose(): void { echo "dispose ", $this->name, "\n"; }
        }

        async function awaitSync(Res $r): void {
            using await ($c = $r) {
                echo "awaited\n";
            }
        }
        """;

    private const string ScopeProbe = """
        <?tyhp
        namespace Probe;

        class Res implements \Tyhp\Contracts\IsDisposable {
            public string $name;
            public function __construct(string $name) { $this->name = $name; }
            public function dispose(): void { echo "dispose ", $this->name, "\n"; }
        }

        function scoped(Res $r): void {
            $held := $r;
            echo "scoped\n";
        }
        """;
}
