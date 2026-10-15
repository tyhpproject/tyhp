using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #433: an <c>Expression</c> argument passed to a <c>\Closure</c> parameter is
/// converted to <c>-&gt;callable</c> however the variable holding it was typed.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionToClosureArgumentEmitterTests
{
    private const string Prelude = """
        <?tyhp
        final class Person {
            public function __construct(public string $name, public int $age) {}
        }
        function takesExpr(\Tyhp\Expression<callable(Person): bool> $e): \Tyhp\Expression<callable(Person): bool> {
            return $e;
        }
        function takesTypedClosure(\Closure $f, Person $p): bool {
            return $f($p) === true;
        }
        """;

    [Fact]
    public void AnnotatedVariable_ExtractsCallable()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(Person $ada): bool {
                \Tyhp\Expression<callable(Person): bool> $e = takesExpr(fn (Person $p) => $p->age > 1);
                return takesTypedClosure($e, $ada);
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("takesTypedClosure($e->callable, $ada)");
    }

    [Fact]
    public void InferredVariable_ExtractsCallable()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(Person $ada): bool {
                $e = takesExpr(fn (Person $p) => $p->age > 1);
                return takesTypedClosure($e, $ada);
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("takesTypedClosure($e->callable, $ada)");
    }

    [Fact]
    public void CallResult_ExtractsCallable()
    {
        var result = SnippetEmit.Compile(Prelude + """

            function run(Person $ada): bool {
                return takesTypedClosure(takesExpr(fn (Person $p) => $p->age > 1), $ada);
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("->callable, $ada)");
    }
}
