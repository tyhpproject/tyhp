using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// The checker (TYHP4322) and the expression-tree emitter must agree on which <c>fn</c> bodies have
/// a tree form: every body the checker accepts is emitted as a <c>->backedBy(fn)</c> chain, and a
/// body the emitter cannot rebuild is an error (TYHP5024), never a plain closure.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionTreeCheckerEmitterParityTests
{
    private const string Prelude = """
        <?tyhp
        class Tag { public string $label = ''; }
        class Item {
            public const string KIND = 'item';
            public string $name = '';
            public ?string $nick = null;
            public int $qty = 0;
            public ?Item $parent = null;
            public array<Tag> $tags = [];
            public array<string, int> $counts = [];
            public function label(): string { return $this->name; }
            public function pad(string $s, int $n = 0): string { return $s; }
            public function many(string ...$xs): string { return ''; }
            public function anyTag(callable(Tag): bool $f): bool { return false; }
            public function tagPath(\Tyhp\PropertyPath<callable(Tag): string> $path): string { return ''; }
            public static function make(string $n): Item { return new Item(); }
        }
        function take(\Tyhp\Expression<callable(Item): mixed> $e): void {}
        """;

    public static TheoryData<string> AcceptedBodies => new()
    {
        "$i->name",
        "$i->parent?->name",
        "$i->label()",
        "$i->parent?->label()",
        "$i->pad('x', 2)",
        "Item::make($i->name)",
        "Item::KIND",
        "Item::class",
        "new Item()",
        "$i->qty + 1 > 2 && !($i->qty === 0)",
        "-$i->qty",
        "$i->name . '!'",
        "$i->nick ?? 'none'",
        "$i->nick ?: 'none'",
        "$i->qty > 0 ? 'some' : 'none'",
        "(string) $i->qty",
        "$i->parent instanceof Item",
        "$i->nick is ?string",
        "$i->counts['a']",
        "[$i->name, 'k' => $i->qty]",
        "$i->anyTag(fn ($t) => $t->label === 'x')",
        "$i->tagPath(fn ($t) => $t->label)",
        "$i->qty > $min",
        "null",
        "1.5",
        "__LINE__",
    };

    [Theory]
    [MemberData(nameof(AcceptedBodies))]
    public void CheckerAcceptedBody_EmitsTreeChain(string body)
    {
        var result = Compile($"function demo(int $min): void {{ take(fn ($i) => {body}); }}");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("take(\\Tyhp\\Expr::");
        result.Php.Should().Contain("->backedBy(fn(\\Item $i)");
        result.Php.Should().NotContain("take(fn");
    }

    [Theory]
    [InlineData("new $cls()", "dynamic class")]
    [InlineData("$cls::make('x')", "dynamic class")]
    [InlineData("$cls::KIND", "dynamic class")]
    [InlineData("$i::class", "dynamic class")]
    [InlineData("$i->$m()", "dynamic member")]
    [InlineData("$i->{$m}", "dynamic member")]
    [InlineData("$i->pad(s: 'x')", "named argument")]
    [InlineData("$i->many(...$xs)", "argument unpacking")]
    public void BodyWithoutTreeForm_Reports4322(string body, string kind)
    {
        var errors = Compile(
            $"function demo(__ClassName<Item> $cls, string $m, array<string> $xs): void {{ take(fn ($i) => {body}); }}").Errors;

        errors.Should().ContainSingle(e => e.StartsWith("4322"))
            .Which.Should().Contain($"`{kind}`");
    }

    [Fact]
    public void ShortTernary_RepeatsConditionAsTrueBranch()
    {
        var result = Compile("function demo(): void { take(fn ($i) => $i->nick ?: 'none'); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(
            "\\Tyhp\\Expr::param('i', \\Item::class)->nick->then()->nick->else('none')->backedBy(");
    }

    [Fact]
    public void NestedPropertyPathFn_EmitsExprClosure()
    {
        var result = Compile("function demo(): void { take(fn ($i) => $i->tagPath(fn ($t) => $t->label)); }");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(
            "->call('tagPath', \\Tyhp\\Expr::closure([\\Tyhp\\Expr::param('t', \\Tag::class)], \\Tyhp\\Expr::param('t', \\Tag::class)->label, 'string'))");
        result.Php.Should().Contain(
            "->backedBy(fn(\\Item $i) => $i->tagPath(\\Tyhp\\PropertyPath::on(\\Tag::class)->label->backedBy(");
    }

    [Fact]
    public void EmitterCannotRebuildBody_Reports5024InsteadOfClosure()
    {
        // With checking skipped, TYHP4322 never runs, so the emitter sees a body it has no tree for.
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["probe.tyhp"] = Prelude + "\nfunction demo(): void { take(fn (Item $i) => isset($i->nick)); }",
                },
                IsolatedCompilation.CreateOptions(tempDir, skipChecking: true));
            result.Diagnostics.Errors.Should().BeEmpty();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["output:phpVersion"] = "8.2" })
                .Build();
            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                new Project(configuration),
                result.RequiresRuntimeGenericTracking,
                expressionTypes: result.ExpressionTypes);
            new TyhpEmitter(context).Emit(result.ParsedFiles!);

            result.Diagnostics.Errors.Should().ContainSingle(d =>
                d.Code == MessageCode.EmitterExpressionTreeUnsupportedNode)
                .Which.Message.Should().Contain("`isset`");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static SnippetEmit.Result Compile(string function) => SnippetEmit.Compile(Prelude + "\n" + function);
}
