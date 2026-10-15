using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Operators declared as tyhpdef <c>extension operator</c> members (the shape <c>tyhp/decimal</c>
/// publishes) are spliced at every call-site position: compound assignment, a free-function call
/// as the left operand, and a non-class left operand that matches a reversed form. The
/// <c>decimal</c> type keyword names <c>\Tyhp\Decimal</c> in a namespaced file without an import.
/// </summary>
[Trait("Category", "Emitter")]
public class TyhpdefExtensionOperatorRewriteTests
{
    private const string DecimalTyhpdef = """
        <?tyhpdef
        namespace Tyhp {
            function decimal(float|int|string|null $value = null): decimal;
            final class Decimal {
                public function add(float|int|string $operand): self;
                public static function one(int $scale = 2): self;
                public static function __add(self|float|int|string $left, self|float|int|string $right): self;
                public static function __subtract(self|float|int|string $left, self|float|int|string $right): self;
                public static function __multiply(self|float|int|string $left, self|float|int|string $right): self;
                public static function __divide(self|float|int|string $left, self|float|int|string $right): self;
                public static function __mod(self|float|int|string $left, self|float|int|string $right): self;
                public static function __pow(self $left, int $right): self;
                public static function __isLessThan(self|float|int|string $left, self|float|int|string $right): bool;
                public static function __isGreaterThan(self|float|int|string $left, self|float|int|string $right): bool;
                public static function __compare(self|float|int|string $left, self|float|int|string $right): int;
                extension operator +(self $left, self|float|int|string $right): self => self::__add($left, $right);
                extension operator -(self $left, self|float|int|string $right): self => self::__subtract($left, $right);
                extension operator *(self $left, self|float|int|string $right): self => self::__multiply($left, $right);
                extension operator /(self $left, self|float|int|string $right): self => self::__divide($left, $right);
                extension operator %(self $left, self|float|int|string $right): self => self::__mod($left, $right);
                extension operator **(self $left, int $right): self => self::__pow($left, $right);
                extension operator >(self $left, self|float|int|string $right): bool => self::__isGreaterThan($left, $right);
                extension operator +(float|int|string $left, self $right): self => self::__add($left, $right);
                extension operator -(float|int|string $left, self $right): self => self::__subtract($left, $right);
                extension operator *(float|int|string $left, self $right): self => self::__multiply($left, $right);
                extension operator <(float|int|string $left, self $right): bool => self::__isLessThan($left, $right);
                extension operator <=>(float|int|string $left, self $right): int => self::__compare($left, $right);
            }
        }
        """;

    [Theory]
    [InlineData("+=", "__add")]
    [InlineData("-=", "__subtract")]
    [InlineData("*=", "__multiply")]
    [InlineData("/=", "__divide")]
    [InlineData("%=", "__mod")]
    public void CompoundAssignment_RewritesToAssignedStaticCall(string compound, string method)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            namespace App;
            use Tyhp\Decimal;
            function f(Decimal $a, Decimal $b): Decimal {
                $a {{compound}} $b;
                return $a;
            }
            """);

        php.Should().Contain($"$a = \\Tyhp\\Decimal::{method}($a, $b);");
        php.Should().NotContain($"$a {compound} $b");
    }

    [Fact]
    public void CompoundAssignment_PowWithIntExponent_RewritesToAssignedStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            use Tyhp\Decimal;
            function f(Decimal $a): Decimal {
                $a **= 2;
                return $a;
            }
            """);

        php.Should().Contain("$a = \\Tyhp\\Decimal::__pow($a, 2);");
        php.Should().NotContain("**=");
    }

    [Fact]
    public void CompoundAssignment_OnPropertyWithScalarRight_RewritesToAssignedStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            final class Cart {
                public decimal $total;
                public function __construct(decimal $total) { $this->total = $total; }
                public function bump(): void {
                    $this->total += 5;
                }
            }
            """);

        php.Should().Contain("$this->total = \\Tyhp\\Decimal::__add($this->total, 5);");
    }

    [Fact]
    public void FreeFunctionCall_AsLeftOperand_IsRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(): bool {
                decimal $sum = \Tyhp\decimal('2') + 1;
                return \Tyhp\decimal('2') > 1 && $sum > 0;
            }
            """);

        php.Should().Contain("\\Tyhp\\Decimal::__add(\\Tyhp\\decimal('2'), 1)");
        php.Should().Contain("\\Tyhp\\Decimal::__isGreaterThan(\\Tyhp\\decimal('2'), 1)");
        php.Should().NotContain("\\Tyhp\\decimal('2') + 1");
        php.Should().NotContain("\\Tyhp\\decimal('2') > 1");
    }

    [Fact]
    public void ScalarLeftOperand_MatchingReversedForm_IsRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function f(decimal $a): void {
                decimal $r1 = 5 + $a;
                decimal $r2 = '1.00' * $a;
                decimal $r3 = 1.5 - $a;
                bool $r4 = 3 < $a;
                int $r5 = 3 <=> $a;
                echo $r1 === $r2;
                echo $r3 === $a;
                echo $r4 === true;
                echo $r5;
            }
            """);

        php.Should().Contain("\\Tyhp\\Decimal::__add(5, $a)");
        php.Should().Contain("\\Tyhp\\Decimal::__multiply('1.00', $a)");
        php.Should().Contain("\\Tyhp\\Decimal::__subtract(1.5, $a)");
        php.Should().Contain("\\Tyhp\\Decimal::__isLessThan(3, $a)");
        php.Should().Contain("\\Tyhp\\Decimal::__compare(3, $a)");
        php.Should().NotContain("5 + $a");
        php.Should().NotContain("3 < $a");
    }

    [Fact]
    public void DecimalKeyword_InNamespacedFileWithoutImport_NamesTyhpDecimal()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            final class Price {
                public decimal $amount;
                public function __construct(decimal $amount) { $this->amount = $amount; }
                public function twice(): decimal {
                    decimal $copy = \Tyhp\decimal('5.00');
                    return $this->amount + $copy;
                }
            }
            """);

        php.Should().Contain("public \\Tyhp\\Decimal $amount;");
        php.Should().Contain("__construct(\\Tyhp\\Decimal $amount)");
        php.Should().Contain("function twice(): \\Tyhp\\Decimal");
        php.Should().Contain("\\Tyhp\\Decimal::__add($this->amount, $copy)");
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
