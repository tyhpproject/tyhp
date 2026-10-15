using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>(decimal) $x</c> types as <c>\Tyhp\Decimal</c> and follows its <c>operator convert</c>
/// declarations: convert-from emits the static <c>__from</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class DecimalCastEmitterTests
{
    private const string DecimalClass = """
        <?tyhp
        namespace Tyhp;

        final class Decimal {
            public function __construct(public string $value = '0') {}

            operator convert(int $value) { return new self((string)$value); }
            operator convert(float $value) { return new self((string)$value); }
            operator convert(string $value) { return new self($value); }
            operator convert(self $value): int { return (int)$value->value; }
            operator convert(self $value): float { return (float)$value->value; }
            operator convert(self $value): string { return $value->value; }
        }
        """;

    [Theory]
    [InlineData("'1.5'", "$b = \\Tyhp\\Decimal::__from('1.5');")]
    [InlineData("3", "$b = \\Tyhp\\Decimal::__from(3);")]
    [InlineData("2.25", "$b = \\Tyhp\\Decimal::__from(2.25);")]
    public void Emit_DecimalCastOfScalar_CallsConvertFrom(string operand, string expected)
    {
        var (errors, php) = Compile($$"""
            <?tyhp
            namespace Probe;

            function make(): void {
                decimal $b = (decimal) {{operand}};
                \var_dump((string) $b);
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain(expected);
        php.Should().NotContain("(decimal)");
    }

    [Fact]
    public void Emit_DecimalCastOfDeclaredScalarVariable_CallsConvertFrom()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace Probe;

            function make(string $text, int $count, float $ratio): void {
                decimal $a = (decimal) $text;
                decimal $b = (decimal) $count;
                decimal $c = (decimal) $ratio;
                \var_dump((string) $a, (string) $b, (string) $c);
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("$a = \\Tyhp\\Decimal::__from($text);");
        php.Should().Contain("$b = \\Tyhp\\Decimal::__from($count);");
        php.Should().Contain("$c = \\Tyhp\\Decimal::__from($ratio);");
    }

    [Fact]
    public void Emit_DecimalCastOfMixed_CallsConvertFrom()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace Probe;

            function make(mixed $value): decimal {
                return (decimal) $value;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("return \\Tyhp\\Decimal::__from($value);");
    }

    [Fact]
    public void Emit_DecimalCastOfDecimal_IsIdentity()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace Probe;

            function make(decimal $value): decimal {
                return (decimal) $value;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("return $value;");
        php.Should().NotContain("__from($value)");
    }

    [Fact]
    public void Emit_ScalarCastsOnDecimal_StillUseConvertTo()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace Probe;

            function make(decimal $value): void {
                int $i = (int) $value;
                float $f = (float) $value;
                string $s = (string) $value;
                \var_dump($i);
                \var_dump($f);
                \var_dump($s);
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("$i = $value->__toInt();");
        php.Should().Contain("$f = $value->__toFloat();");
        php.Should().Contain("$s = $value->__toString();");
    }

    [Fact]
    public void Check_DecimalCastResultAssignsToDecimalVariable()
    {
        var (errors, _) = Compile("""
            <?tyhp
            namespace Probe;

            function make(): decimal {
                decimal $b = (decimal) '1.5';
                decimal $c = (decimal) 3;
                return $b;
            }
            """);

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bool $value", "bool")]
    [InlineData("array<int, int> $value", "array<int, int>")]
    [InlineData("?string $value", "?string")]
    public void Check_DecimalCastOfTypeWithoutConvertFrom_ReportsMismatch(string parameter, string operandType)
    {
        var (errors, _) = Compile($$"""
            <?tyhp
            namespace Probe;

            function make({{parameter}}): decimal {
                return (decimal) $value;
            }
            """);

        errors.Should().ContainSingle(e => e.Contains("4008") && e.Contains(operandType) && e.Contains("Decimal"));
    }

    [Fact]
    public void Check_DecimalCastOfUnionOfConvertibleScalars_IsAccepted()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace Probe;

            function make(int|string $value): decimal {
                return (decimal) $value;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("return \\Tyhp\\Decimal::__from($value);");
    }

    private static (List<string> Errors, string Php) Compile(string probe)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var decimalPath = Path.Combine(tempDir, "decimal.tyhp");
        var probePath = Path.Combine(tempDir, "probe.tyhp");
        File.WriteAllText(decimalPath, DecimalClass);
        File.WriteAllText(probePath, probe);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [decimalPath, probePath],
                IsolatedCompilation.CreateOptions(tempDir));

            var errors = result.Diagnostics.Errors
                .Select(d => $"{(int)d.Code}: {d.Message}")
                .ToList();
            if (errors.Count > 0)
            {
                return (errors, string.Empty);
            }

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, CreateProject());
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return (errors, string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.2",
            })
            .Build();
        return new Project(configuration);
    }
}
