using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// The first-class callable of a source overload set (and of an invokable object) is a wrapper
/// that converts per parameter position: a type some signature accepts as written is never
/// converted, any other type that converts to a signature's parameter is, and a type that would
/// convert to different types in different signatures is rejected at the reference (TYHP4376).
/// </summary>
[Trait("Category", "Emitter")]
public class OverloadSetCallableWrapperTests
{
    private const string Library = """
        <?tyhp
        namespace Probe;

        final class Money {
            public function __construct(public int $cents) {}
            operator convert(int $value) { return new self($value); }
        }

        final class Meters {
            public function __construct(public int $value) {}
            operator convert(int $value) { return new self($value); }
        }

        function describe(Money $m): string;
        function describe(string $label, int $cents): string;
        function describe(Money|string $a, int $cents = 0): string {
            if ($a instanceof Money) {
                return 'money:' . $a->cents;
            }
            return 'str:' . $a . ':' . $cents;
        }

        function pick(Money $m): string;
        function pick(int $n): string;
        function pick(Money|int $a): string {
            if ($a instanceof Money) {
                return 'money:' . $a->cents;
            }
            return 'int:' . $a;
        }

        function shown(Money $m): string;
        function shown(Money $m, string $suffix): string;
        function shown(Money $m, string $suffix = ''): string {
            return 'shown:' . $m->cents . $suffix;
        }

        function join(Money $a, string $b): string;
        function join(string $a, Money $b): string;
        function join(Money|string $a, Money|string $b): string {
            if ($a instanceof Money && \is_string($b)) {
                return 'M' . $a->cents . $b;
            }
            if (\is_string($a) && $b instanceof Money) {
                return $a . 'M' . $b->cents;
            }
            return 'other';
        }

        class Printer {
            public function show(Money $m): string;
            public function show(string $s): string;
            public function show(Money|string $a): string {
                return $a instanceof Money ? 'money:' . $a->cents : 'str:' . $a;
            }

            public static function stat(Money $m): string;
            public static function stat(string $s): string;
            public static function stat(Money|string $a): string {
                return $a instanceof Money ? 'smoney:' . $a->cents : 'sstr:' . $a;
            }
        }

        class Doubler {
            public function __invoke(Money $m): int { return $m->cents * 2; }
        }

        function fnDescribe(): \Closure { return describe(...); }
        function fnPick(): \Closure { return pick(...); }
        function fnShown(): \Closure { return shown(...); }
        function fnJoin(): \Closure { return join(...); }
        function fnShow(): \Closure { Printer $p = new Printer(); return $p->show(...); }
        function fnStat(): \Closure { return Printer::stat(...); }
        function fnInvoke(): \Closure { Doubler $d = new Doubler(); return $d(...); }
        """;

    private static (string Php, string Output, IReadOnlyList<string> Errors) Run(string entry)
    {
        var result = SnippetEmit.Compile(Library);
        if (result.Errors.Count > 0)
        {
            return (result.Php, "", result.Errors);
        }

        return (result.Php, EmittedPhpRunner.Run(result.Files!, entry), result.Errors);
    }

    [Fact]
    public void Run_OverloadSet_ConvertsTheTypeThatConvertsAndPassesTheRestThrough()
    {
        var (php, output, errors) = Run(
            @"$f = \Probe\fnDescribe(); echo $f(5), ',', $f('a', 2), ',', $f(new \Probe\Money(3)), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("money:5,str:a:2,money:3\n", php);
    }

    [Fact]
    public void Emit_OverloadSet_CallsTheImplementationFromTheWrapper()
    {
        var result = SnippetEmit.Compile(Library);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("static fn(");
        result.Php.Should().Contain("Money::__from(");
    }

    [Fact]
    public void Run_TypeAnOverloadAcceptsAsWritten_IsNeverConverted()
    {
        // `int` is accepted as written by `pick(int)`, so it is not converted to Money for `pick(Money)`.
        var (php, output, errors) = Run(
            @"$f = \Probe\fnPick(); echo $f(5), ',', $f(new \Probe\Money(2)), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("int:5,money:2\n", php);
    }

    [Fact]
    public void Emit_TypeAnOverloadAcceptsAsWritten_KeepsTheNativeCallable()
    {
        var result = SnippetEmit.Compile(Library);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("return pick(...);");
    }

    [Fact]
    public void Run_SameTargetInSeveralSignatures_IsNotAmbiguous()
    {
        var (php, output, errors) = Run(
            @"$f = \Probe\fnShown(); echo $f(5), ',', $f(6, 'x'), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("shown:5,shown:6x\n", php);
    }

    [Fact]
    public void Run_ConversionIsDecidedPerPosition()
    {
        var (php, output, errors) = Run(
            @"$f = \Probe\fnJoin(); echo $f(5, 'x'), ',', $f('x', 6), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("M5x,xM6\n", php);
    }

    [Fact]
    public void Run_InstanceAndStaticMethodSets_Convert()
    {
        var (php, output, errors) = Run(
            @"$i = \Probe\fnShow(); $s = \Probe\fnStat(); echo $i(5), ',', $i('a'), ',', $s(6), ',', $s('b'), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("money:5,str:a,smoney:6,sstr:b\n", php);
    }

    [Fact]
    public void Run_InvokableObjectCallable_ConvertsTheInvokeParameter()
    {
        var (php, output, errors) = Run(@"$f = \Probe\fnInvoke(); echo $f(4), ',', $f(new \Probe\Money(5)), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("8,10\n", php);
    }

    [Fact]
    public void Compile_TypeThatConvertsToDifferentTypesInDifferentSignatures_IsRejectedAtTheReference()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Probe;

            final class Money {
                public function __construct(public int $cents) {}
                operator convert(int $value) { return new self($value); }
            }

            final class Meters {
                public function __construct(public int $value) {}
                operator convert(int $value) { return new self($value); }
            }

            function measure(Money $m): string;
            function measure(Meters $m): string;
            function measure(Money|Meters $a): string {
                return 'measured';
            }

            function run(): \Closure {
                return measure(...);
            }
            """);

        result.Errors.Should().Contain(e => e.StartsWith("4376:"), string.Join("; ", result.Errors));
    }

    [Fact]
    public void Compile_AmbiguousConversionAtOnePosition_DoesNotRejectTheOtherPositions()
    {
        // Position 1 converts int to Money in one signature only, so only position 0 is ambiguous.
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Probe;

            final class Money {
                public function __construct(public int $cents) {}
                operator convert(int $value) { return new self($value); }
            }

            final class Meters {
                public function __construct(public int $value) {}
                operator convert(int $value) { return new self($value); }
            }

            function pair(Money $a, string $b): string;
            function pair(Meters $a, Money $b): string;
            function pair(Money|Meters $a, Money|string $b): string {
                return 'pair';
            }

            function run(): \Closure {
                return pair(...);
            }
            """);

        result.Errors.Where(e => e.StartsWith("4376:")).Should().HaveCount(1, string.Join("; ", result.Errors));
    }
}
