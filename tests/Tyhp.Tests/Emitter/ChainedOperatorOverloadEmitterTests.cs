using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// An operator overload nested inside another operator keeps its checked type after it is
/// rewritten, so the outer operator is rewritten too (<c>$a + $b + $c</c> is never
/// <c>\Tyhp\Decimal::__add($a, $b) + $c</c>). The overloads are the thin tyhpdef
/// <c>extension operator</c> forms that <c>tyhp/decimal</c> publishes.
/// </summary>
[Trait("Category", "Emitter")]
public class ChainedOperatorOverloadEmitterTests
{
    private const string DecimalTyhpdef = """
        <?tyhpdef
        namespace Tyhp {
            function decimal(float|int|string|null $value = null): decimal;
            final class Decimal {
                public function withScale(int $scale): self;
                public function __toString(): string;
                public static function one(int $scale = 2): self;
                public static function max(self ...$values): self;
                public static function __negate(self $value): self;
                public static function __add(self|float|int|string $left, self|float|int|string $right): self;
                public static function __subtract(self|float|int|string $left, self|float|int|string $right): self;
                public static function __multiply(self|float|int|string $left, self|float|int|string $right): self;
                public static function __divide(self|float|int|string $left, self|float|int|string $right): self;
                public static function __pow(self $left, int $right): self;
                public static function __isEqual(self|float|int|string $left, self|float|int|string $right): bool;
                public static function __isLessThan(self|float|int|string $left, self|float|int|string $right): bool;
                public static function __isGreaterThan(self|float|int|string $left, self|float|int|string $right): bool;
                extension operator -(self $value): self => self::__negate($value);
                extension operator +(self $left, self|float|int|string $right): self => self::__add($left, $right);
                extension operator -(self $left, self|float|int|string $right): self => self::__subtract($left, $right);
                extension operator *(self $left, self|float|int|string $right): self => self::__multiply($left, $right);
                extension operator /(self $left, self|float|int|string $right): self => self::__divide($left, $right);
                extension operator **(self $left, int $right): self => self::__pow($left, $right);
                extension operator ==(self $left, self|float|int|string $right): bool => self::__isEqual($left, $right);
                extension operator <(self $left, self|float|int|string $right): bool => self::__isLessThan($left, $right);
                extension operator >(self $left, self|float|int|string $right): bool => self::__isGreaterThan($left, $right);
                extension operator +(float|int|string $left, self $right): self => self::__add($left, $right);
                extension operator -(float|int|string $left, self $right): self => self::__subtract($left, $right);
                extension operator *(float|int|string $left, self $right): self => self::__multiply($left, $right);
            }
        }
        """;

    private const string Add = @"\Tyhp\Decimal::__add(";
    private const string Subtract = @"\Tyhp\Decimal::__subtract(";
    private const string Multiply = @"\Tyhp\Decimal::__multiply(";

    [Theory]
    [InlineData(
        "$a + $b + $c",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c)")]
    [InlineData(
        "$a + $b + $c + $d",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c), $d)")]
    [InlineData(
        "$a * ($b + $c)",
        @"\Tyhp\Decimal::__multiply($a, \Tyhp\Decimal::__add($b, $c))")]
    [InlineData(
        "($a + $b) * ($c - $d)",
        @"\Tyhp\Decimal::__multiply(\Tyhp\Decimal::__add($a, $b), \Tyhp\Decimal::__subtract($c, $d))")]
    [InlineData(
        "-$a + $b",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__negate($a), $b)")]
    [InlineData(
        "-($a + $b) * $c",
        @"\Tyhp\Decimal::__multiply(\Tyhp\Decimal::__negate(\Tyhp\Decimal::__add($a, $b)), $c)")]
    [InlineData(
        "$a ** 2 + $b",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__pow($a, 2), $b)")]
    [InlineData(
        "$a + $b ** 2",
        @"\Tyhp\Decimal::__add($a, \Tyhp\Decimal::__pow($b, 2))")]
    [InlineData(
        "$a + 5 + $b",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, 5), $b)")]
    [InlineData(
        "5 + $a + $b",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__add(5, $a), $b)")]
    [InlineData(
        "10 - ($a + $b)",
        @"\Tyhp\Decimal::__subtract(10, \Tyhp\Decimal::__add($a, $b))")]
    [InlineData(
        "10 - $a - $b",
        @"\Tyhp\Decimal::__subtract(\Tyhp\Decimal::__subtract(10, $a), $b)")]
    [InlineData(
        "$a * $b + $c * $d",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__multiply($a, $b), \Tyhp\Decimal::__multiply($c, $d))")]
    [InlineData(
        "$a->withScale(4) * $b + $c",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__multiply($a->withScale(4), $b), $c)")]
    [InlineData(
        "$a + $b->withScale(4) * $c",
        @"\Tyhp\Decimal::__add($a, \Tyhp\Decimal::__multiply($b->withScale(4), $c))")]
    [InlineData(
        "($a + $b + $c)->withScale(1)",
        @"(\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c))->withScale(1)")]
    [InlineData(
        @"\Tyhp\Decimal::one() + $a + $b",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__add(\Tyhp\Decimal::one(), $a), $b)")]
    [InlineData(
        @"\Tyhp\decimal('1') + $a + $b",
        @"\Tyhp\Decimal::__add(\Tyhp\Decimal::__add(\Tyhp\decimal('1'), $a), $b)")]
    public void Return_NestedOperators_EveryLevelIsRewritten(string expression, string expected)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            namespace App;
            function f(decimal $a, decimal $b, decimal $c, decimal $d): decimal {
                return {{expression}};
            }
            """);

        php.Should().Contain($"return {expected};");
    }

    [Theory]
    [InlineData(
        "$a + $b > $c",
        @"\Tyhp\Decimal::__isGreaterThan(\Tyhp\Decimal::__add($a, $b), $c)")]
    [InlineData(
        "$a * $b < $c + $d",
        @"\Tyhp\Decimal::__isLessThan(\Tyhp\Decimal::__multiply($a, $b), \Tyhp\Decimal::__add($c, $d))")]
    [InlineData(
        "$a + $b == $c + 1",
        @"\Tyhp\Decimal::__isEqual(\Tyhp\Decimal::__add($a, $b), \Tyhp\Decimal::__add($c, 1))")]
    [InlineData(
        "$a - $b - $c > $d",
        @"\Tyhp\Decimal::__isGreaterThan(\Tyhp\Decimal::__subtract(\Tyhp\Decimal::__subtract($a, $b), $c), $d)")]
    public void Return_NestedOperatorInComparison_EveryLevelIsRewritten(string expression, string expected)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            namespace App;
            function f(decimal $a, decimal $b, decimal $c, decimal $d): bool {
                return {{expression}};
            }
            """);

        php.Should().Contain($"return {expected};");
    }

    [Fact]
    public void CompoundAssignment_WithNestedOperatorOnRight_RewritesEveryLevel()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(decimal $x, decimal $a, decimal $b, decimal $c): decimal {
                $x += $a * $b + $c;
                $x -= $a + $b + $c;
                return $x;
            }
            """);

        php.Should().Contain(
            @"$x = \Tyhp\Decimal::__add($x, \Tyhp\Decimal::__add(\Tyhp\Decimal::__multiply($a, $b), $c));");
        php.Should().Contain(
            @"$x = \Tyhp\Decimal::__subtract($x, \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c));");
    }

    [Fact]
    public void PropertyOperand_InNestedOperators_RewritesEveryLevel()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            final class Cart {
                public decimal $total;
                public function __construct(decimal $total) { $this->total = $total; }
                public function bump(decimal $a, decimal $b, decimal $c): void {
                    $this->total += $a * $b + $c;
                }
                public function projected(decimal $a, decimal $b): decimal {
                    return $this->total + $a * $b + $a;
                }
            }
            """);

        php.Should().Contain(
            @"$this->total = \Tyhp\Decimal::__add($this->total, \Tyhp\Decimal::__add(\Tyhp\Decimal::__multiply($a, $b), $c));");
        php.Should().Contain(
            @"return \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($this->total, \Tyhp\Decimal::__multiply($a, $b)), $a);");
    }

    [Fact]
    public void CallArguments_WithNestedOperators_AreRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function double(decimal $v): decimal { return $v * 2; }
            function f(decimal $a, decimal $b, decimal $c, decimal $d): decimal {
                decimal $one = double($a + $b + $c);
                decimal $two = double(double($a + $b) + $c);
                decimal $three = \Tyhp\Decimal::max($a + $b + $c, $c * $d - $a);
                return $one + $two + $three;
            }
            """);

        php.Should().Contain(
            @"$one = double(\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c));");
        php.Should().Contain(
            @"$two = double(\Tyhp\Decimal::__add(double(\Tyhp\Decimal::__add($a, $b)), $c));");
        php.Should().Contain(
            @"$three = \Tyhp\Decimal::max(\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c), \Tyhp\Decimal::__subtract(\Tyhp\Decimal::__multiply($c, $d), $a));");
        php.Should().Contain(
            @"return \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($one, $two), $three);");
    }

    [Fact]
    public void Ternary_WithNestedOperators_BranchesAndConditionAreRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(bool $flag, decimal $a, decimal $b, decimal $c): decimal {
                decimal $picked = $flag ? $a + $b + $c : $a * $b - $c;
                return ($a + $b > $c) ? $a + $b + $c : $a;
            }
            """);

        php.Should().Contain(
            @"$picked = $flag ? \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c) : \Tyhp\Decimal::__subtract(\Tyhp\Decimal::__multiply($a, $b), $c);");
        php.Should().Contain(
            @"return \Tyhp\Decimal::__isGreaterThan(\Tyhp\Decimal::__add($a, $b), $c) ? \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c) : $a;");
    }

    [Fact]
    public void ArrayLiteral_WithNestedOperators_ElementsAreRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(decimal $a, decimal $b, decimal $c): decimal {
                array<int, decimal> $list = [$a + $b + $c, $a * $b + $c, -$a + $b];
                return $list[0] + $list[1] + $list[2];
            }
            """);

        php.Should().Contain(
            @"$list = [\Tyhp\Decimal::__add(\Tyhp\Decimal::__add($a, $b), $c), \Tyhp\Decimal::__add(\Tyhp\Decimal::__multiply($a, $b), $c), \Tyhp\Decimal::__add(\Tyhp\Decimal::__negate($a), $b)];");
        php.Should().Contain(
            @"return \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($list[0], $list[1]), $list[2]);");
    }

    [Fact]
    public void Closures_WithNestedOperators_AreRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(decimal $a, decimal $b): void {
                $sum3 = function (decimal $p, decimal $q, decimal $r): decimal {
                    return $p + $q + $r;
                };
                $mixed = fn(decimal $p, decimal $q): decimal => $p * $q + $a;
                $nested = fn(decimal $p): decimal => ($p + $a) * ($p - $b);
                echo $sum3($a, $b, $a) === $mixed($a, $b);
                echo $nested($a) === $mixed($b, $a);
            }
            """);

        php.Should().Contain(
            @"return \Tyhp\Decimal::__add(\Tyhp\Decimal::__add($p, $q), $r);");
        php.Should().Contain(
            @"=> \Tyhp\Decimal::__add(\Tyhp\Decimal::__multiply($p, $q), $a);");
        php.Should().Contain(
            @"=> \Tyhp\Decimal::__multiply(\Tyhp\Decimal::__add($p, $a), \Tyhp\Decimal::__subtract($p, $b));");
    }

    [Fact]
    public void Interpolation_CallWithNestedOperators_KeepsBracesAndRewritesOperators()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            final class Holder {
                public function twice(decimal $v): decimal { return $v * 2; }
            }
            function f(Holder $h, decimal $a, decimal $b, decimal $c): string {
                return "sum={$h->twice($a + $b + $c)}";
            }
            """);

        php.Should().Contain(
            "\"sum={$h->twice(\\Tyhp\\Decimal::__add(\\Tyhp\\Decimal::__add($a, $b), $c))}\"");
    }

    [Fact]
    public void NativeOperandsAreNotAffected()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(int $i, int $j, int $k, float $x, decimal $a): float {
                int $sum = $i + $j + $k;
                float $mixed = $x * $i + $j;
                return $mixed + $sum;
            }
            """);

        php.Should().Contain("$sum = ($i + $j) + $k;");
        php.Should().Contain("$mixed = ($x * $i) + $j;");
        php.Should().NotContain(Add);
        php.Should().NotContain(Subtract);
        php.Should().NotContain(Multiply);
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "decimal.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(tyhpdefPath, DecimalTyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["output:phpVersion"] = "8.4" })
                .Build();
            var project = new Project(configuration);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [tyhpdefPath, tyhpPath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
