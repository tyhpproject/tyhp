using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #430: an <c>fn</c> passed to an <c>Expression&lt;…&gt;</c> parameter is converted to
/// an expression tree when the call is a <c>foreach</c> subject.
/// </summary>
[Trait("Category", "Emitter")]
public class ForeachSubjectExpressionArgumentEmitterTests
{
    private const string Prelude = """
        <?tyhp
        final class Person {
            public function __construct(public string $name, public int $age) {}
        }
        final class FeRepo {
            /** @return string[] */
            public static function names(\Tyhp\Expression<callable(Person): bool> $predicate): array<string> {
                return [];
            }
        }
        """;

    [Fact]
    public void Foreach_StaticCallSubject_ConvertsFnToExpression()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(): void {
                foreach (FeRepo::names(fn (Person $p) => $p->age > 18) as $name) {
                    echo $name;
                }
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("foreach (FeRepo::names(new \\Tyhp\\Expression(");
    }
}
