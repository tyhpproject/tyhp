using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Argument-driven emission (an inline <c>fn</c> becoming an <c>Expression</c> tree, an
/// <c>Expression</c> value becoming <c>-&gt;callable</c>) follows the parameter type of the
/// <em>selected declared signature</em> of an overload set, not the implementation's widened one.
/// </summary>
[Trait("Category", "Overloads")]
public class SourceOverloadCallArgumentEmitterTests
{
    private const string Prelude = """
        <?tyhp
        final class Person {
            public function __construct(public string $name, public int $age) {}
        }
        """;

    private static string Compile(string body)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        return result.Php;
    }

    [Fact]
    public void FunctionOverload_InlineFnBecomesAnExpressionWhenTheSelectedSignatureTakesOne()
    {
        var php = Compile("""
            function choose(\Tyhp\Expression<callable(Person): bool> $e): string;
            function choose(int $limit): string;
            function choose(\Tyhp\Expression<callable(Person): bool>|int $x): string {
                return 'x';
            }

            function run(): string {
                return choose(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().Contain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void FunctionOverload_ExpressionSignature_WinsOverTheImplementationUnionOrder()
    {
        var php = Compile("""
            function choose(\Tyhp\Expression<callable(Person): bool> $e): string;
            function choose(callable(Person): bool $f, int $n): string;
            function choose((callable(Person): bool)|\Tyhp\Expression<callable(Person): bool> $x, int $n = 0): string {
                return 'x';
            }

            function run(): string {
                return choose(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().Contain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void FunctionOverload_CallableSignature_WinsOverTheImplementationUnionOrder()
    {
        var php = Compile("""
            function choose(callable(Person): bool $f): string;
            function choose(\Tyhp\Expression<callable(Person): bool> $e, int $n): string;
            function choose(\Tyhp\Expression<callable(Person): bool>|(callable(Person): bool) $x, int $n = 0): string {
                return 'x';
            }

            function run(): string {
                return choose(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().NotContain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void FunctionOverload_InlineFnStaysAClosureWhenTheSelectedSignatureTakesACallable()
    {
        var php = Compile("""
            function choose(\Tyhp\Expression<callable(Person): bool> $e, int $n): string;
            function choose(callable(Person): bool $f): string;
            function choose((callable(Person): bool)|\Tyhp\Expression<callable(Person): bool> $x, int $n = 0): string {
                return 'x';
            }

            function run(): string {
                return choose(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().NotContain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void MethodOverload_InlineFnBecomesAnExpressionWhenTheSelectedSignatureTakesOne()
    {
        var php = Compile("""
            final class Query {
                public function where(\Tyhp\Expression<callable(Person): bool> $e): string;
                public function where(int $limit): string;
                public function where(\Tyhp\Expression<callable(Person): bool>|int $x): string {
                    return 'x';
                }
            }

            function run(Query $q): string {
                return $q->where(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().Contain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void FunctionOverload_ExactArgument_IsNotConvertedByAWiderImplementationParameter()
    {
        var php = Compile("""
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators extends Money {
                operator convert (int $value) {
                    Money $m = new Money();
                    $m->amount = $value;
                    return $m;
                }
            }
            function takeAmount(int $n): string;
            function takeAmount(Money $m, int $extra): string;
            function takeAmount(int|Money $x, int $extra = 0): string {
                return 'x';
            }

            function pass(int $n): string {
                return takeAmount($n);
            }
            """);

        php.Should().Contain("takeAmount($n)");
        php.Should().NotContain("takeAmount(\\MoneyOperators::__from");
    }

    [Fact]
    public void FunctionOverload_SameArity_ExpressionSignatureSelected_BuildsTheTree()
    {
        var php = Compile("""
            function choose(\Tyhp\Expression<callable(Person): bool> $e): string;
            function choose(callable(Person): bool $f): string;
            function choose((callable(Person): bool)|\Tyhp\Expression<callable(Person): bool> $x): string {
                return 'x';
            }

            function run(): string {
                return choose(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().Contain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void FunctionOverload_SameArity_CallableSignatureSelected_StaysAClosure()
    {
        var php = Compile("""
            function choose(callable(Person): bool $f): string;
            function choose(\Tyhp\Expression<callable(Person): bool> $e): string;
            function choose(\Tyhp\Expression<callable(Person): bool>|(callable(Person): bool) $x): string {
                return 'x';
            }

            function run(): string {
                return choose(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().NotContain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void MethodOverload_SameArity_CallableSignatureSelected_StaysAClosure()
    {
        var php = Compile("""
            final class Query {
                public function where(callable(Person): bool $f): string;
                public function where(\Tyhp\Expression<callable(Person): bool> $e): string;
                public function where(\Tyhp\Expression<callable(Person): bool>|(callable(Person): bool) $x): string {
                    return 'x';
                }
            }

            function run(Query $q): string {
                return $q->where(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().NotContain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void StaticMethodOverload_SameArity_CallableSignatureSelected_StaysAClosure()
    {
        var php = Compile("""
            final class Query {
                public static function where(callable(Person): bool $f): string;
                public static function where(\Tyhp\Expression<callable(Person): bool> $e): string;
                public static function where(\Tyhp\Expression<callable(Person): bool>|(callable(Person): bool) $x): string {
                    return 'x';
                }
            }

            function run(): string {
                return Query::where(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().NotContain("new \\Tyhp\\Expression(");
    }

    [Fact]
    public void StaticMethodOverload_SameArity_ExpressionSignatureSelected_BuildsTheTree()
    {
        var php = Compile("""
            final class Query {
                public static function where(\Tyhp\Expression<callable(Person): bool> $e): string;
                public static function where(callable(Person): bool $f): string;
                public static function where((callable(Person): bool)|\Tyhp\Expression<callable(Person): bool> $x): string {
                    return 'x';
                }
            }

            function run(): string {
                return Query::where(fn (Person $p) => $p->age > 1);
            }
            """);

        php.Should().Contain("new \\Tyhp\\Expression(");
    }
}
