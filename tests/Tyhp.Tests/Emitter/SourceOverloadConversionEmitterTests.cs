using System.Text.RegularExpressions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// A call that selects a source overload signature only through implicit <c>operator convert</c>
/// emits the same conversion rewrite a plain call applies, against the selected signature, and
/// the emitted PHP keeps a single function that runs.
/// </summary>
[Trait("Category", "Overloads")]
public class SourceOverloadConversionEmitterTests
{
    private const string Probe = """
        <?tyhp
        namespace Probe;

        final class Money {
            public function __construct(public int $cents) {}
            operator convert(int $value) { return new self($value); }
        }

        function takeMoney(Money $m): string;
        function takeMoney(string $label, int $cents): string;
        function takeMoney(Money|string $a, int $cents = 0): string {
            return $a instanceof Money ? 'money:' . $a->cents : 'label:' . $a . ':' . $cents;
        }

        function size(Money $m): string;
        function size(int $n): string;
        function size(Money|int $a): string {
            return $a instanceof Money ? 'money' : 'int:' . $a;
        }

        function split(Money $a, Money $b): string;
        function split(Money $a, int $b): string;
        function split(Money $a, Money|int $b): string {
            return $b instanceof Money ? 'both' : 'second-int:' . $b;
        }

        final class Till {
            public function take(Money $m): string;
            public function take(string $label, int $cents): string;
            public function take(Money|string $a, int $cents = 0): string {
                return $a instanceof Money ? 'till:' . $a->cents : 'label';
            }

            public static function deposit(Money $m): string;
            public static function deposit(string $label, int $cents): string;
            public static function deposit(Money|string $a, int $cents = 0): string {
                return $a instanceof Money ? 'deposit:' . $a->cents : 'label';
            }

            public function __invoke(Money $m): string;
            public function __invoke(string $label, int $cents): string;
            public function __invoke(Money|string $a, int $cents = 0): string {
                return $a instanceof Money ? 'invoke:' . $a->cents : 'label';
            }
        }

        function run(): void {
            echo takeMoney(5), "\n";
            echo takeMoney('tip', 3), "\n";
            echo size(7), "\n";
            echo size(new Money(9)), "\n";
            echo split(1, 2), "\n";
            $till = new Till();
            echo $till->take(11), "\n";
            echo Till::deposit(12), "\n";
            echo $till->__invoke(13), "\n";
            echo $till(14), "\n";
        }
        """;

    [Fact]
    public void Emit_ConversionOnlyCall_RewritesTheArgumentAgainstTheSelectedSignature()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        Regex.Matches(result.Php, @"function takeMoney\(").Count.Should().Be(1, result.Php);
        result.Php.Should().MatchRegex(@"takeMoney\(\\Probe\\Money::__from\(5\)\)");
        result.Php.Should().MatchRegex(@"takeMoney\('tip', 3\)");
    }

    [Fact]
    public void Emit_ExactMatch_IsNotConverted()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().MatchRegex(@"size\(7\)");
        result.Php.Should().NotMatchRegex(@"size\(\\Probe\\Money::__from");
    }

    [Fact]
    public void Emit_ConvertToScalar_RewritesToTheConversionMethod()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Probe;

            final class Celsius {
                public function __construct(public int $degrees) {}
                operator convert(self $value): int { return $value->degrees; }
            }

            function takesInt(int $n): string;
            function takesInt(string $s, int $extra): string;
            function takesInt(int|string $a, int $extra = 0): string {
                return 'int:' . $a . ':' . $extra;
            }

            function run(Celsius $c): string {
                return takesInt($c);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        Regex.Matches(result.Php, @"function takesInt\(").Count.Should().Be(1, result.Php);
        result.Php.Should().MatchRegex(@"takesInt\(\$c->__toInt\(\)\)");
    }

    [Fact]
    public void Emit_MethodStaticMethodAndInvoke_RewriteTheConversion()
    {
        var result = SnippetEmit.Compile(Probe);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().MatchRegex(@"->take\(\\Probe\\Money::__from\(11\)\)");
        result.Php.Should().MatchRegex(@"Till::deposit\(\\Probe\\Money::__from\(12\)\)");
        result.Php.Should().MatchRegex(@"->__invoke\(\\Probe\\Money::__from\(13\)\)");
        result.Php.Should().MatchRegex(@"\$till\(\\Probe\\Money::__from\(14\)\)");
    }

    [Fact]
    public void Run_InvokeCallOnAPlainInvokableObject_ConvertsItsArgument()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Probe;

            final class Money {
                public function __construct(public int $cents) {}
                operator convert(int $value) { return new self($value); }
            }

            final class Printer {
                public function __invoke(Money $m): string {
                    return 'printed:' . $m->cents;
                }
            }

            function run(): void {
                $p = new Printer();
                echo $p(15), "\n";
            }
            """);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().MatchRegex(@"\$p\(\\Probe\\Money::__from\(15\)\)");

        var output = EmittedPhpRunner.Run(result.Files!, @"\Probe\run();");

        output.Should().Be("printed:15\n");
    }

    [Fact]
    public void Run_ConversionSelectedCalls_ExecuteTheImplementation()
    {
        var result = SnippetEmit.Compile(Probe);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));

        var output = EmittedPhpRunner.Run(result.Files!, @"\Probe\run();");

        output.Should().Be(
            "money:5\nlabel:tip:3\nint:7\nmoney\nsecond-int:2\ntill:11\ndeposit:12\ninvoke:13\ninvoke:14\n");
    }
}
