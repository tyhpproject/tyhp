using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Found bugs #432 and #434: <c>Expression&lt;callable(T): R&gt;</c> parameters infer generics from
/// an inline <c>fn</c>, and an <c>Expression</c> value is accepted for a <c>callable</c> parameter.
/// </summary>
[Trait("Category", "Checker")]
public class ExpressionCallableAssignabilityTests
{
    private const string Person = """
        <?tyhp
        final class Person {
            public function __construct(public string $firstName, public int $age) {}
        }
        """;

    [Fact]
    public void ExpressionParameter_InfersMethodGenericFromInlineFn()
    {
        var result = SnippetEmit.Compile(Person + """

            final class TreeQuery<T> {
                public function __construct(public string $label = 'q') {}
                public function select<R>(\Tyhp\Expression<callable(T): R> $selector): TreeQuery<R> {
                    return new TreeQuery<R>();
                }
            }
            function run(TreeQuery<Person> $q): TreeQuery<string> {
                return $q->select(fn ($p) => $p->firstName);
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CallableParameter_InfersFunctionGenericFromInlineFn_Baseline()
    {
        var result = SnippetEmit.Compile(Person + """

            function pick<R>(callable(Person): R $selector): R {
                return $selector(new Person('a', 1));
            }
            function run(): string {
                return pick(fn (Person $p) => $p->firstName);
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CallableParameter_InfersMethodGenericFromInlineFn_Baseline()
    {
        var result = SnippetEmit.Compile(Person + """

            final class TreeQuery<T> {
                public function __construct(public string $label = 'q') {}
                public function select<R>(callable(T): R $selector): TreeQuery<R> {
                    return new TreeQuery<R>();
                }
            }
            function run(TreeQuery<Person> $q): TreeQuery<string> {
                return $q->select(fn (Person $p) => $p->firstName);
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ExpressionParameter_InfersFunctionGenericFromInlineFn()
    {
        var result = SnippetEmit.Compile(Person + """

            function project<R>(\Tyhp\Expression<callable(Person): R> $selector): R {
                return ($selector->callable)(new Person('a', 1));
            }
            function run(): string {
                return project(fn (Person $p) => $p->firstName);
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ExpressionArgument_IsAcceptedForCallableParameter()
    {
        var result = SnippetEmit.Compile(Person + """

            function takesExpr(\Tyhp\Expression<callable(Person): bool> $e): \Tyhp\Expression<callable(Person): bool> {
                return $e;
            }
            function takesCallable(callable(Person): bool $f, Person $p): bool {
                return $f($p);
            }
            function run(Person $ada): bool {
                $e = takesExpr(fn (Person $p) => $p->age > 1);
                return takesCallable($e, $ada);
            }
            """);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ExpressionArgument_WithDifferentSignature_StillRejectedForCallableParameter()
    {
        var result = SnippetEmit.Compile(Person + """

            function takesExpr(\Tyhp\Expression<callable(Person): string> $e): \Tyhp\Expression<callable(Person): string> {
                return $e;
            }
            function takesCallable(callable(Person): bool $f, Person $p): bool {
                return $f($p);
            }
            function run(Person $ada): bool {
                $e = takesExpr(fn (Person $p) => $p->firstName);
                return takesCallable($e, $ada);
            }
            """);

        result.Errors.Should().Contain(e => e.StartsWith("4010"));
    }
}
