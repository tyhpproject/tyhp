using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Executes emitted <c>Expression</c> / <c>PropertyPath</c> values against the built
/// <c>tyhp/lambda</c> runtime: invoking the value, and running its compiled tree. Skips when PHP or
/// the runtime dist for the local PHP version is not available.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionTreeInvokeRuntimeTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Expression;
        use Tyhp\PropertyPath;

        class Tag {
            public string $label = '';
        }

        class Person {
            public const string KIND = 'person';
            public string $name;
            public int $age;
            public ?Person $boss;
            public ?string $nick = null;

            public function __construct(string $name, int $age, ?Person $boss = null) {
                $this->name = $name;
                $this->age = $age;
                $this->boss = $boss;
            }

            public static function named(string $name): Person { return new Person($name, 0); }

            public function tagPath(PropertyPath<callable(Tag): string> $path): string {
                return $path->getPath();
            }
        }

        class Roster {
            public function test(Expression<callable(Person): bool> $predicate, Person $person): bool {
                return $predicate($person);
            }

            public function pick(PropertyPath<callable(Person): ?string> $path, Person $person): ?string {
                return $path($person);
            }

            public function column(PropertyPath<callable(Person): string> $path): string {
                return $path->getPath();
            }

            /** Dumps the invoked value, then the value from the compiled tree. */
            public function both(Expression<callable(Person): mixed> $e, Person $person): void {
                \var_dump($e($person), $e->compile()($person));
            }
        }
        """;

    [Fact]
    public void InvokingCompiledExpressionAndPropertyPath_ReturnsCallableResult()
    {
        var output = Run("""
            function run(): void {
                $roster = new Roster();
                $ada = new Person('Ada', 36);
                $kid = new Person('Kid', 9, $ada);

                \var_dump($roster->test(fn ($p) => $p->age >= 18, $ada));
                \var_dump($roster->test(fn ($p) => $p->age >= 18, $kid));
                \var_dump($roster->pick(fn ($p) => $p->name, $kid));
                \var_dump($roster->pick(fn ($p) => $p->boss?->name, $kid));
                \var_dump($roster->pick(fn ($p) => $p->boss?->name, $ada));
                \var_dump($roster->column(fn ($p) => $p->name));

                Expression<callable(Person): bool> $adult = fn ($p) => $p->age >= 18;
                \var_dump($adult($kid));
            }
            """);
        if (output is null)
        {
            return;
        }

        output.Should().Be("""
            bool(true)
            bool(false)
            string(3) "Kid"
            string(3) "Ada"
            NULL
            string(4) "name"
            bool(false)

            """);
    }

    [Fact]
    public void ShortTernary_InvokeAndCompiledTreeAgree()
    {
        var output = Run("""
            function run(): void {
                $roster = new Roster();
                $named = new Person('Ada', 36);
                $named->nick = 'ace';
                $roster->both(fn ($p) => $p->nick ?: 'none', $named);
                $roster->both(fn ($p) => $p->nick ?: 'none', new Person('Kid', 9));
            }
            """);
        if (output is null)
        {
            return;
        }

        output.Should().Be("""
            string(3) "ace"
            string(3) "ace"
            string(4) "none"
            string(4) "none"

            """);
    }

    [Fact]
    public void NestedPropertyPathFn_InvokeAndCompiledTreeAgree()
    {
        var output = Run("""
            function run(): void {
                (new Roster())->both(fn ($p) => $p->tagPath(fn ($t) => $t->label), new Person('Ada', 36));
            }
            """);
        if (output is null)
        {
            return;
        }

        output.Should().Be("""
            string(5) "label"
            string(5) "label"

            """);
    }

    [Fact]
    public void NamedClassStaticAccess_InvokeAndCompiledTreeAgree()
    {
        var output = Run("""
            function run(): void {
                $roster = new Roster();
                $ada = new Person('Ada', 36);
                $roster->both(fn ($p) => Person::KIND, $ada);
                $roster->both(fn ($p) => Person::named($p->name)->name, $ada);
                $roster->both(fn ($p) => (new Person($p->name, 1))->age, $ada);
            }
            """);
        if (output is null)
        {
            return;
        }

        output.Should().Be("""
            string(6) "person"
            string(6) "person"
            string(3) "Ada"
            string(3) "Ada"
            int(1)
            int(1)

            """);
    }

    private static string? Run(string function)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + function);
        result.Errors.Should().BeEmpty();
        return EmittedPhpRunner.RunAgainstRuntimeDist(result.Files!, "\\Probe\\run();", "core", "lambda");
    }
}
