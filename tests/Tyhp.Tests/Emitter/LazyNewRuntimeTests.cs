using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Runs <c>lazy new</c> output on the PHP on <c>PATH</c>. Every case needs PHP 8.4+ (native lazy
/// objects) and returns early otherwise. The assertions are about observable behavior: the
/// constructor is deferred, runs once on the same instance, sees the class's type arguments, and
/// arguments are evaluated at the <c>lazy new</c> expression.
/// </summary>
[Trait("Category", "Emitter")]
public class LazyNewRuntimeTests
{
    private const string State = """
        $state = static fn (object $o): string => (new \ReflectionClass($o))->isUninitializedLazyObject($o)
            ? "lazy" : "initialized";
        """;

    [Fact]
    public void NonGeneric_ConstructorIsDeferredAndRunsOnTheSameInstance()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class User {
                public int $id;
                public function __construct(int $id) {
                    echo "ctor ", $id, "\n";
                    $this->id = $id;
                }
            }

            function make(int $id): User {
                return lazy new User($id);
            }
            """, State + """

            $u = \Probe\make(7);
            echo $state($u), "\n";
            echo $u->id, "\n";
            echo $state($u), "\n";
            """);

        output?.Should().Be("lazy\nctor 7\n7\ninitialized\n");
    }

    [Fact]
    public void ArgumentsUseCallerLocalsAndRunEagerlyInOrder()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Pair {
                public int $a;
                public int $b;
                public function __construct(int $a, int $b) {
                    echo "ctor\n";
                    $this->a = $a;
                    $this->b = $b;
                }
            }

            function tick(string $label, int $value): int {
                echo "arg ", $label, "\n";
                return $value;
            }

            function make(int $local): Pair {
                $p = lazy new Pair(tick('a', $local), tick('b', $local + 1));
                echo "built\n";
                return $p;
            }
            """, """
            $p = \Probe\make(10);
            echo "touch\n";
            echo $p->a, ",", $p->b, "\n";
            """);

        output?.Should().Be("arg a\narg b\nbuilt\ntouch\nctor\n10,11\n");
    }

    [Fact]
    public void ThrowingArgument_ThrowsAtTheExpressionLikeNew()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Pair {
                public int $a;
                public int $b;
                public function __construct(int $a, int $b) {
                    echo "ctor\n";
                    $this->a = $a;
                    $this->b = $b;
                }
            }

            function tick(string $label): int {
                echo "arg ", $label, "\n";
                return 1;
            }

            function boom(): int {
                echo "arg boom\n";
                throw new \RuntimeException('boom');
            }

            function make(): void {
                try {
                    $p = lazy new Pair(tick('first'), boom());
                    echo "unreachable\n";
                } catch (\RuntimeException $e) {
                    echo "caught ", $e->getMessage(), "\n";
                }
            }
            """, """
            \Probe\make();
            """);

        output?.Should().Be("arg first\narg boom\ncaught boom\n");
    }

    [Fact]
    public void SelfWithPrivateConstructor_BuildsAGhostInsideTheClass()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Single {
                public int $n;
                private function __construct(int $n) {
                    echo "ctor\n";
                    $this->n = $n;
                }
                public static function make(int $n): self {
                    return lazy new self($n);
                }
            }
            """, State + """

            $s = \Probe\Single::make(3);
            echo $state($s), "\n";
            echo $s->n, "\n";
            """);

        output?.Should().Be("lazy\nctor\n3\n");
    }

    [Fact]
    public void GenericWithOnlyTheBag_IsAGhostAndTheConstructorSeesTheTypeArgument()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                public function __construct(T $item) {
                    echo "ctor ", (string) typeof(T), "\n";
                }

                public function kind(): string {
                    return (string) typeof(T);
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """, State + """

            $b = \Probe\make();
            echo $state($b), "\n";
            echo $b->kind(), "\n";
            echo $state($b), "\n";
            """);

        // `kind()` reads the bag, which is the class's only stored property, so it initializes.
        output?.Should().Be("lazy\nctor int\nint\ninitialized\n");
    }

    [Fact]
    public void GenericWithAuthorProperty_ConstructorSeesTheTypeArgument()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                public int $n;
                public function __construct(T $item) {
                    echo "ctor ", (string) typeof(T), "\n";
                    $this->n = 1;
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """, State + """

            $b = \Probe\make();
            echo $state($b), "\n";
            echo $b->n, "\n";
            echo $state($b), "\n";
            """);

        output?.Should().Be("lazy\nctor int\n1\ninitialized\n");
    }

    [Fact]
    public void ReadonlyGeneric_BootsTheBagAndSeesTheTypeArgument()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            readonly class Box<T> {
                public function __construct(public int $n) {
                    echo "ctor ", (string) typeof(T), "\n";
                }
            }

            function make(): Box<string> {
                return lazy new Box<string>(5);
            }
            """, State + """

            $b = \Probe\make();
            echo $state($b), "\n";
            echo $b->n, "\n";
            echo $state($b), "\n";
            """);

        output?.Should().Be("lazy\nctor string\n5\ninitialized\n");
    }

    [Fact]
    public void GenericSubclassChain_BindsEveryLevelBeforeTheConstructor()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Base<A> {
                public string $tag;
                public function __construct(string $tag) {
                    echo "base ", (string) typeof(A), "\n";
                    $this->tag = $tag;
                }
                public function baseA(): string {
                    return (string) typeof(A);
                }
            }

            class Leaf<B> extends Base<string> {
                public function __construct(string $tag): parent($tag) {
                    echo "leaf ", (string) typeof(B), "\n";
                }
            }

            function make(): Leaf<int> {
                return lazy new Leaf<int>('t');
            }
            """, State + """

            $l = \Probe\make();
            echo $state($l), "\n";
            echo $l->tag, "\n";
            echo $state($l), "\n";
            echo $l->baseA(), "\n";
            """);

        output?.Should().Be("lazy\nbase string\nleaf int\nt\ninitialized\nstring\n");
    }

    [Fact]
    public void NonGenericSubclassOfGeneric_BindsParentArguments()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Base<A> {
                public string $tag;
                public function __construct(string $tag) {
                    echo "base ", (string) typeof(A), "\n";
                    $this->tag = $tag;
                }
            }

            class IntBase extends Base<int> {
                public function __construct(string $tag): parent($tag) {
                    echo "intbase\n";
                }
            }

            function make(): IntBase {
                return lazy new IntBase('t');
            }
            """, State + """

            $l = \Probe\make();
            echo $state($l), "\n";
            echo $l->tag, "\n";
            """);

        output?.Should().Be("lazy\nbase int\nintbase\nt\n");
    }

    [Fact]
    public void ClassWithoutConstructor_IsALazyGhostWhoseInitializerDoesNothing()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Plain {
                public int $n = 4;
            }

            function make(): Plain {
                return lazy new Plain();
            }
            """, State + """

            $p = \Probe\make();
            echo $state($p), "\n";
            echo $p->n, "\n";
            echo $state($p), "\n";
            """);

        output?.Should().Be("lazy\n4\ninitialized\n");
    }

    [Fact]
    public void NamedAndSpreadArguments_ReachTheConstructorAsWritten()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Range {
                public int $lo;
                public int $hi;
                public int $step;
                public function __construct(int $lo, int $hi, int $step = 1) {
                    $this->lo = $lo;
                    $this->hi = $hi;
                    $this->step = $step;
                }
            }

            function named(): Range {
                return lazy new Range(hi: 9, lo: 2);
            }

            function spread(array<int> $parts): Range {
                return lazy new Range(...$parts);
            }
            """, """
            $a = \Probe\named();
            echo $a->lo, ",", $a->hi, ",", $a->step, "\n";
            $b = \Probe\spread([1, 5, 2]);
            echo $b->lo, ",", $b->hi, ",", $b->step, "\n";
            """);

        output?.Should().Be("2,9,1\n1,5,2\n");
    }

    [Fact]
    public void StaticTarget_ConstructsTheLateBoundClass()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Model {
                public int $id = 0;
                public function __construct() {}
                public static function make(): static {
                    return lazy new static();
                }
            }

            class Post extends Model {}
            """, """
            $p = \Probe\Post::make();
            echo \get_class($p), "\n";
            """);

        output?.Should().Be("Probe\\Post\n");
    }

    [Fact]
    public void ClassWithNoStoredProperty_RunsTheConstructorEagerly()
    {
        var output = RunOn84("""
            <?tyhp
            namespace Probe;

            class Stateless {
                public function __construct() {
                    echo "ctor\n";
                }
            }

            function make(): Stateless {
                return lazy new Stateless();
            }
            """, """
            $s = \Probe\make();
            echo "made\n";
            """);

        output?.Should().Be("ctor\nmade\n");
    }

    private static string? RunOn84(string tyhp, string entryPoint)
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return null;
        }

        var result = SnippetEmit.Compile(tyhp, "8.4");
        result.Errors.Should().BeEmpty();

        var probe = EmittedPhpRunner.Run(
            [],
            "echo \\PHP_VERSION_ID >= 80400 ? 'yes' : 'no';");
        return probe == "yes" ? EmittedPhpRunner.Run(result.Files!, entryPoint) : null;
    }
}
