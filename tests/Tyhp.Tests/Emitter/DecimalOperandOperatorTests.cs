using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Operator overloads whose right operand is a union or an interface-typed alias
/// (<c>DecimalCoercible</c>) accept every operand whose members are all accepted: a class that
/// implements the interface, the alias itself, and scalar unions. A class that declares
/// <c>operator convert(self): decimal</c> is a <c>DecimalConvertible</c> for the checker.
/// </summary>
[Trait("Category", "Emitter")]
public class DecimalOperandOperatorTests
{
    private const string DecimalTyhpdef = """
        <?tyhpdef
        namespace Tyhp\Contracts {
            interface DecimalConvertible {
                public function __toDecimal(): \Tyhp\Decimal;
            }
        }
        namespace Tyhp {
            type DecimalCoercible = float|int|string|\Tyhp\Contracts\DecimalConvertible;
            function decimal(float|int|string|\Tyhp\Contracts\DecimalConvertible|null $value = null): \Tyhp\Decimal;
            final class Decimal implements \Tyhp\Contracts\DecimalConvertible {
                public function add(\Tyhp\DecimalCoercible $operand): self;
                public function __toDecimal(): self;
                public static function __add(self $left, self $right): self;
                public static function __subtract(self $left, self $right): self;
                public static function __multiply(self $left, self $right): self;
                public static function __divide(self $left, self $right): self;
                public static function __mod(self $left, self $right): self;
                public static function __isEqual(self $left, self|\Tyhp\DecimalCoercible $right): bool;
                public static function __isNotEqual(self $left, self|\Tyhp\DecimalCoercible $right): bool;
                public static function __isLessThan(self $left, self|\Tyhp\DecimalCoercible $right): bool;
                public static function __isLessThanOrEqual(self $left, self|\Tyhp\DecimalCoercible $right): bool;
                public static function __isGreaterThan(self $left, self|\Tyhp\DecimalCoercible $right): bool;
                public static function __isGreaterThanOrEqual(self $left, self|\Tyhp\DecimalCoercible $right): bool;
                public static function __compare(self $left, self|\Tyhp\DecimalCoercible $right): int;
                extension operator +(self $left, self $right): self => self::__add($left, $right);
                extension operator +(self $left, \Tyhp\DecimalCoercible $right): self => self::__add($left, $right);
                extension operator -(self $left, self $right): self => self::__subtract($left, $right);
                extension operator -(self $left, \Tyhp\DecimalCoercible $right): self => self::__subtract($left, $right);
                extension operator *(self $left, \Tyhp\DecimalCoercible $right): self => self::__multiply($left, $right);
                extension operator /(self $left, \Tyhp\DecimalCoercible $right): self => self::__divide($left, $right);
                extension operator %(self $left, \Tyhp\DecimalCoercible $right): self => self::__mod($left, $right);
                extension operator ==(self $left, self|\Tyhp\DecimalCoercible $right): bool => self::__isEqual($left, $right);
                extension operator !=(self $left, self|\Tyhp\DecimalCoercible $right): bool => self::__isNotEqual($left, $right);
                extension operator <(self $left, self|\Tyhp\DecimalCoercible $right): bool => self::__isLessThan($left, $right);
                extension operator <=(self $left, self|\Tyhp\DecimalCoercible $right): bool => self::__isLessThanOrEqual($left, $right);
                extension operator >(self $left, self|\Tyhp\DecimalCoercible $right): bool => self::__isGreaterThan($left, $right);
                extension operator >=(self $left, self|\Tyhp\DecimalCoercible $right): bool => self::__isGreaterThanOrEqual($left, $right);
                extension operator <=>(self $left, self|\Tyhp\DecimalCoercible $right): int => self::__compare($left, $right);
                extension operator +(float|int|string $left, self $right): self => self::__add($left, $right);
                extension operator -(float|int|string $left, self $right): self => self::__subtract($left, $right);
                extension operator *(float|int|string $left, self $right): self => self::__multiply($left, $right);
                extension operator /(float|int|string $left, self $right): self => self::__divide($left, $right);
                extension operator %(float|int|string $left, self $right): self => self::__mod($left, $right);
                extension operator convert(int $value): self => self::__from($value);
                public static function __from(int|float|string $from): self;
            }
        }
        """;

    private const string Cents = """
        final class Cents implements \Tyhp\Contracts\DecimalConvertible {
            public function __construct(public int $cents) {}
            public function __toDecimal(): \Tyhp\Decimal { return new \Tyhp\Decimal(); }
        }
        """;

    [Theory]
    [InlineData("==", "__isEqual")]
    [InlineData("!=", "__isNotEqual")]
    [InlineData("<", "__isLessThan")]
    [InlineData("<=", "__isLessThanOrEqual")]
    [InlineData(">", "__isGreaterThan")]
    [InlineData(">=", "__isGreaterThanOrEqual")]
    public void Comparison_RightOperandIsClassImplementingDecimalConvertible_CallsStaticOperator(
        string op,
        string method)
    {
        var (errors, php) = Compile($$"""
            <?tyhp
            namespace App;
            {{Cents}}
            function f(decimal $a, Cents $p): bool {
                return $a {{op}} $p;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain($"\\Tyhp\\Decimal::{method}($a, $p)");
        php.Should().NotContain($"$a {op} $p");
    }

    [Fact]
    public void Spaceship_RightOperandIsClassImplementingDecimalConvertible_CallsCompare()
    {
        var (errors, php) = Compile($$"""
            <?tyhp
            namespace App;
            {{Cents}}
            function f(decimal $a, Cents $p): int {
                return $a <=> $p;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("\\Tyhp\\Decimal::__compare($a, $p)");
    }

    [Theory]
    [InlineData("+", "__add")]
    [InlineData("-", "__subtract")]
    [InlineData("*", "__multiply")]
    [InlineData("/", "__divide")]
    [InlineData("%", "__mod")]
    public void Arithmetic_RightOperandIsClassImplementingDecimalConvertible_IsAccepted(string op, string method)
    {
        var (errors, php) = Compile($$"""
            <?tyhp
            namespace App;
            {{Cents}}
            function f(decimal $a, Cents $p): decimal {
                return $a {{op}} $p;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain($"\\Tyhp\\Decimal::{method}($a, $p)");
    }

    [Theory]
    [InlineData("\\Tyhp\\DecimalCoercible $p")]
    [InlineData("float|int|string $p")]
    [InlineData("int|string $p")]
    [InlineData("\\Tyhp\\Contracts\\DecimalConvertible $p")]
    public void Arithmetic_RightOperandIsUnionAcceptedByTheOverload_IsAccepted(string parameter)
    {
        var (errors, php) = Compile($$"""
            <?tyhp
            namespace App;
            function f(decimal $a, {{parameter}}): void {
                decimal $sum = $a + $p;
                decimal $diff = $a - $p;
                decimal $prod = $a * $p;
                decimal $quot = $a / $p;
                decimal $rem = $a % $p;
                echo $sum === $diff;
                echo $prod === $quot;
                echo $rem === $sum;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("\\Tyhp\\Decimal::__add($a, $p)");
        php.Should().Contain("\\Tyhp\\Decimal::__subtract($a, $p)");
        php.Should().Contain("\\Tyhp\\Decimal::__multiply($a, $p)");
        php.Should().Contain("\\Tyhp\\Decimal::__divide($a, $p)");
        php.Should().Contain("\\Tyhp\\Decimal::__mod($a, $p)");
    }

    [Theory]
    [InlineData("float|int|string $p")]
    [InlineData("int|string $p")]
    public void Arithmetic_LeftOperandIsScalarUnion_IsAccepted(string parameter)
    {
        var (errors, php) = Compile($$"""
            <?tyhp
            namespace App;
            function f(decimal $a, {{parameter}}): decimal {
                return $p + $a;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("\\Tyhp\\Decimal::__add($p, $a)");
    }

    [Fact]
    public void Arithmetic_UnionMemberRejectedByTheOverload_StillReportsTyhp4029()
    {
        var (errors, _) = Compile("""
            <?tyhp
            namespace App;
            function f(decimal $a, int|bool $p): void {
                echo $a + $p;
            }
            """);

        errors.Should().Contain(e => e.Contains("4029"));
    }

    [Fact]
    public void Comparison_RightOperandIsUnrelatedClass_StillReportsOrStaysNative()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace App;
            final class Plain {}
            function f(decimal $a, Plain $p): void {
                echo $a + $p;
            }
            """);

        errors.Should().Contain(e => e.Contains("4029"));
        php.Should().BeEmpty();
    }

    [Fact]
    public void ConvertOperatorToDecimal_ClassIsDecimalConvertible()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace App;
            final class Pennies {
                public function __construct(public int $n) {}
                operator convert(self $value): decimal {
                    return new \Tyhp\Decimal();
                }
            }
            function takes(decimal $d): decimal { return $d; }
            function f(decimal $d, Pennies $c): void {
                decimal $one = \Tyhp\decimal($c);
                decimal $two = $d->add($c);
                decimal $three = takes((decimal) $c);
                echo $one === $two;
                echo $three === $d;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("implements \\Tyhp\\Contracts\\DecimalConvertible");
    }

    private static (List<string> Errors, string Php) Compile(string probe)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "decimal.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(tyhpdefPath, DecimalTyhpdef);
        File.WriteAllText(tyhpPath, probe);

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

            var errors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Select(d => $"{(int)d.Code}: {d.Message}")
                .ToList();
            if (errors.Count > 0)
            {
                return (errors, string.Empty);
            }

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return (errors, string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
