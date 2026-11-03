using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Expression trees with a nested arrow fn, an array literal, or a null-safe method call in the body.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionTreeNestedLambdaArrayNullSafeTests
{
    private const string Prelude = """
        <?tyhp
        class Post {
            public int $rating = 0;
            public string $title = '';
        }
        class Posts {
            public function any(\Tyhp\Expression<callable(Post): bool>|(callable(Post): bool) $p): bool { return false; }
            public function sum(callable(Post): int $f): int { return 0; }
        }
        class Blog {
            public int $id = 0;
            public string $name = '';
            public Posts $posts;
            public ?Blog $parent = null;
            public function __construct() { $this->posts = new Posts(); }
            public function title(): string { return ''; }
            public function hasIdIn(array<int> $ids): bool { return false; }
        }
        function take(\Tyhp\Expression<callable(Blog): mixed> $e): void {}
        function take2(\Tyhp\Expression<callable(Blog, int): mixed> $e): void {}
        """;

    [Fact]
    public void NestedFn_UnionCallableParameter_EmitsExprClosure()
    {
        var result = Compile("function demo(): void { take(fn ($b) => $b->posts->any(fn ($p) => $p->rating > 3)); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(
            "\\Tyhp\\Expr::param('b', \\Blog::class)->posts->call('any', \\Tyhp\\Expr::closure([\\Tyhp\\Expr::param('p', \\Post::class)], \\Tyhp\\Expr::param('p', \\Post::class)->rating->gt(3), 'bool'))");
        result.Php.Should().Contain(
            "->backedBy(fn(\\Blog $b) => $b->posts->any(\\Tyhp\\Expr::param('p', \\Post::class)->rating->gt(3)->backedBy(fn(\\Post $p): bool => $p->rating > 3)))");
    }

    [Fact]
    public void NestedFn_CallableParameter_InfersParameterType()
    {
        var result = Compile("function demo(): void { take(fn ($b) => $b->posts->sum(fn ($p) => $p->rating)); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(
            "->call('sum', \\Tyhp\\Expr::closure([\\Tyhp\\Expr::param('p', \\Post::class)], \\Tyhp\\Expr::param('p', \\Post::class)->rating, 'int'))");
    }

    [Fact]
    public void NestedFn_ReferencesOuterParameterAndCapture()
    {
        var result = Compile("""
            function demo(int $min): void {
                take2(fn ($b, $n) => $b->posts->any(fn ($p) => $p->rating > $n && $p->rating > $min));
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("\\Tyhp\\Expr::param('n', 'int', 1)");
        result.Php.Should().Contain("\\Tyhp\\Expr::param('p', \\Post::class, 0)->rating->gt(\\Tyhp\\Expr::param('n', 'int', 1))");
        result.Php.Should().Contain("->gt($min)");
    }

    [Fact]
    public void NestedFn_CaptureInsideNestedBody_IsCheckedAsCapture()
    {
        var errors = Compile("function demo(): void { take(fn ($b) => $b->posts->any(fn ($p) => $p->rating > $undefined)); }").Errors;

        errors.Should().Contain(e => e.StartsWith("4323") || e.Contains("undefined"));
    }

    [Theory]
    [InlineData("function ($p) { return true; }", "closure")]
    [InlineData("fn (&$p) => true", "by-reference parameter")]
    [InlineData("fn (Post ...$p) => true", "variadic parameter")]
    [InlineData("fn ($p = null) => true", "parameter default value")]
    [InlineData("fn &($p) => true", "by-reference fn")]
    public void NestedFn_UnsupportedForm_Reports4322(string nested, string kind)
    {
        var errors = Compile($"function demo(): void {{ take(fn ($b) => $b->posts->any({nested})); }}").Errors;

        errors.Should().Contain(e => e.StartsWith("4322") && e.Contains($"`{kind}`"));
    }

    [Fact]
    public void NestedFn_UnsupportedBody_Reports4322Once()
    {
        var errors = Compile("function demo(): void { take(fn ($b) => $b->posts->any(fn ($p) => isset($p->title))); }").Errors;

        errors.Where(e => e.StartsWith("4322")).Should().ContainSingle()
            .Which.Should().Contain("`isset`");
    }

    [Fact]
    public void KeyedArrayLiteral_EmitsArrayOfWithKeyList()
    {
        var result = Compile("function demo(): void { take(fn ($b) => ['id' => $b->id, 'name' => $b->name]); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(
            "\\Tyhp\\Expr::arrayOf([\\Tyhp\\Expr::param('b', \\Blog::class)->id, \\Tyhp\\Expr::param('b', \\Blog::class)->name], ['id', 'name'])->backedBy(");
    }

    [Fact]
    public void ListAndNestedArrayLiteral_EmitsArrayOf()
    {
        var result = Compile("function demo(): void { take(fn ($b) => [$b->id, [1, 'k' => $b->name]]); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(
            "\\Tyhp\\Expr::arrayOf([\\Tyhp\\Expr::param('b', \\Blog::class)->id, \\Tyhp\\Expr::arrayOf([1, \\Tyhp\\Expr::param('b', \\Blog::class)->name], [1 => 'k'])])");
    }

    [Fact]
    public void ArrayLiteralAsMethodArgument_EmitsArrayOf()
    {
        var result = Compile("function demo(int $x): void { take(fn ($b) => $b->hasIdIn([1, 2, $x])); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("->call('hasIdIn', \\Tyhp\\Expr::arrayOf([1, 2, $x]))");
    }

    [Fact]
    public void ArraySpread_Reports4322()
    {
        var errors = Compile("function demo(array<int> $xs): void { take(fn ($b) => [...$xs]); }").Errors;

        errors.Should().ContainSingle(e => e.StartsWith("4322") && e.Contains("`array spread`"));
    }

    [Fact]
    public void NullSafeMethodCall_EmitsNullSafeCall()
    {
        var result = Compile("function demo(): void { take(fn ($b) => $b->parent?->title()); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("\\Tyhp\\Expr::param('b', \\Blog::class)->parent->nullSafe()->call('title')->backedBy(fn(\\Blog $b) => $b->parent?->title())");
    }

    [Fact]
    public void NullSafeMethodCallInsideCoalesce_EmitsNullSafeCall()
    {
        var result = Compile("function demo(): void { take(fn ($b) => $b->parent?->title() ?? 'none'); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("->parent->nullSafe()->call('title')->coalesce('none')");
    }

    private static SnippetEmit.Result Compile(string function) => SnippetEmit.Compile(Prelude + "\n" + function);
}
