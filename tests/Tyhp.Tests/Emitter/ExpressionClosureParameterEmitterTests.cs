using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// An Expression or PropertyPath parameter accepts a Closure in emitted PHP.
/// An inline fn argument is still a fluent tree.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionClosureParameterEmitterTests
{
    [Fact]
    public void ParameterHintAcceptsClosureAndBodyCallsFromClosure()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            class User {
                public int $age = 0;
                public string $name = '';
            }
            class Query {
                public \Tyhp\Expression<callable(User): bool> $stored;
                public function where(\Tyhp\Expression<callable(User): bool> $predicate): void {}
                public function orderBy(\Tyhp\PropertyPath<callable(User): string> $path): void {}
                public function id(\Tyhp\Expression<callable(User): bool> $predicate): \Tyhp\Expression<callable(User): bool> {
                    return $predicate;
                }
                public function maybe(?\Tyhp\Expression<callable(User): bool> $predicate): void {}
                public function write(\Tyhp\Expression<callable(User): bool> &$predicate): void {}
                public function keep<T>(\Tyhp\Expression<callable(User): bool> $predicate): T {
                    return default(T);
                }
            }
            class Box {
                public function __construct(public \Tyhp\Expression<callable(User): bool> $predicate) {}
            }
            function demo(Query $q): void {
                $q->where(fn ($u) => $u->age > 1);
                $q->orderBy(fn ($u) => $u->name);
            }
            """);

        result.Errors.Should().BeEmpty();
        var php = result.Php;

        php.Should().Contain("function where(\\Tyhp\\Expression|\\Closure $predicate): void");
        php.Should().Contain("if ($predicate instanceof \\Closure) {");
        php.Should().Contain("\\Tyhp\\Expression::fromClosure($predicate, 'callable(");
        php.Should().Contain("\\User");

        php.Should().Contain("function orderBy(\\Tyhp\\PropertyPath|\\Closure $path): void");
        php.Should().Contain("\\Tyhp\\PropertyPath::fromClosure($path, 'callable(");

        php.Should().Contain("function id(\\Tyhp\\Expression|\\Closure $predicate): \\Tyhp\\Expression");
        php.Should().NotContain("function id(\\Tyhp\\Expression|\\Closure $predicate): \\Tyhp\\Expression|\\Closure");

        php.Should().Contain("function maybe(\\Tyhp\\Expression|\\Closure|null $predicate): void");
        php.Should().Contain("function write(\\Tyhp\\Expression &$predicate): void");
        php.Should().NotContain("function write(\\Tyhp\\Expression|\\Closure");

        php.Should().Contain("public \\Tyhp\\Expression $stored");
        php.Should().NotContain("public \\Tyhp\\Expression|\\Closure $stored");
        php.Should().Contain("public \\Tyhp\\Expression $predicate");
        php.Should().NotContain("public \\Tyhp\\Expression|\\Closure $predicate");

        php.Should().Contain("function keep(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().NotContain("function keep(\\Tyhp\\Expression");
        php.Should().Contain("return function (\\Tyhp\\Expression|\\Closure $predicate) use ($T): mixed {");
        php.Should().Contain("if ($T === null) {");

        php.Should().Contain("->backedBy(fn");
        php.Should().Contain("where(\\Tyhp\\Expr::");
        php.Should().Contain("orderBy(\\Tyhp\\PropertyPath::on(");
    }

    [Fact]
    public void LayoutTwoFunctionAndMethodConvertTheClosureAfterTypeArgumentInference()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            class User {
                public int $age = 0;
            }
            class Query {
                public function keep<T>(\Tyhp\Expression<callable(User): bool> $predicate): T {
                    return default(T);
                }
            }
            function keepFn<T>(\Tyhp\Expression<callable(User): bool> $predicate): T {
                return default(T);
            }
            """);

        result.Errors.Should().BeEmpty();
        var php = result.Php;

        php.Should().Contain("function keepFn(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().Contain("function keep(\\Tyhp\\TypeLike|string|null $T = null): \\Closure");
        php.Should().NotContain("function keepFn(\\Tyhp\\Expression");
        php.Should().NotContain("function keep(\\Tyhp\\Expression");

        // Each value closure infers the type argument first, then converts the closure.
        var searchFrom = 0;
        var closures = 0;
        while (true)
        {
            var open = php.IndexOf("return function (\\Tyhp\\Expression|\\Closure $predicate) use ($T): mixed {", searchFrom, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            closures++;
            var inference = php.IndexOf("if ($T === null) {", open, StringComparison.Ordinal);
            var prologue = php.IndexOf("if ($predicate instanceof \\Closure) {", open, StringComparison.Ordinal);
            inference.Should().BeGreaterThan(open);
            prologue.Should().BeGreaterThan(inference);
            php.IndexOf("\\Tyhp\\Expression::fromClosure($predicate, 'callable(", prologue, StringComparison.Ordinal)
                .Should().BeGreaterThan(prologue);
            searchFrom = open + 1;
        }

        closures.Should().Be(2);
    }

    [Fact]
    public void GenericClassForwardingSignaturesDoNotWidenAPromotedParameter()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            class User {
                public int $age = 0;
            }
            class Holder<T> {
                public function __construct(public \Tyhp\Expression<callable(User): bool> $predicate, public T $value) {}
            }
            class Plain<T> {
                public function __construct(\Tyhp\Expression<callable(User): bool> $predicate, public T $value) {}
            }
            """);

        result.Errors.Should().BeEmpty();
        var php = result.Php;

        // The promoted property is the parameter, and the constructor never converts it.
        php.Should().Contain("function __construct(public \\Tyhp\\Expression $predicate");
        php.Should().Contain("new_Holder__tyhpGeneric(?\\Tyhp\\Type $__generic_T, \\Tyhp\\Expression $predicate, mixed $value)");
        php.Should().Contain("return function (\\Tyhp\\Expression $predicate, mixed $value) use ($__tyhpType_T): static {");

        // A plain constructor parameter converts inside the constructor, so every signature widens.
        php.Should().Contain("function __construct(\\Tyhp\\Expression|\\Closure $predicate, mixed $value)");
        php.Should().Contain("new_Plain__tyhpGeneric(?\\Tyhp\\Type $__generic_T, \\Tyhp\\Expression|\\Closure $predicate, mixed $value)");
        php.Should().Contain("\\Tyhp\\Expression::fromClosure($predicate, 'callable(");
    }

    [Fact]
    public void VariadicPhpTypeAndInlineClosureParametersStayAsSpelled()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            class User {
                public int $age = 0;
            }
            class Query {
                public function many(\Tyhp\Expression<callable(User): bool> ...$predicates): void {}
                public function pinned(#[\Tyhp\PhpType('mixed')] \Tyhp\Expression<callable(User): bool> $predicate): void {}
            }
            function run(): void {
                $inline = fn (\Tyhp\Expression<callable(User): bool> $e): bool => true;
                $block = function (\Tyhp\PropertyPath<callable(User): int> $p): bool {
                    return true;
                };
            }
            """);

        result.Errors.Should().BeEmpty();
        var php = result.Php;

        php.Should().Contain("function many(\\Tyhp\\Expression ...$predicates): void");
        php.Should().Contain("function pinned(mixed $predicate): void");
        php.Should().NotContain("\\Tyhp\\Expression|\\Closure $e");
        php.Should().NotContain("\\Tyhp\\PropertyPath|\\Closure $p");
        php.Should().NotContain("fromClosure($e");
        php.Should().NotContain("fromClosure($p");
        php.Should().NotContain("fromClosure($predicates");
    }
}
