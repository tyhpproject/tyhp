using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// A first-class callable whose parameter accepts implicitly convertible types is emitted as a
/// wrapper closure that applies the conversion; it runs in PHP like any callable.
/// </summary>
[Trait("Category", "Emitter")]
public class CallableWrapperEmitterTests
{
    private const string Library = """
        <?tyhp
        namespace Probe;

        class Wallet {
            public int $amount = 0;
            operator convert(int $value): self {
                Wallet $w = new Wallet();
                $w->amount = $value;
                return $w;
            }
            operator convert(string $value): self {
                Wallet $w = new Wallet();
                $w->amount = \strlen($value);
                return $w;
            }
        }

        class Money {
            public int $cents = 9;
            operator convert(self $value): int => $value->cents;
        }

        function takeWallet(Wallet $w): int { return $w->amount; }
        function takeInt(int $n): int { return $n; }
        function takeMaybe(?Wallet $w): int { return $w === null ? -1 : $w->amount; }
        function scaled(Wallet $w, int $by = 2): int { return $w->amount * $by; }
        function logIt(Wallet $w): void { echo 'log:', $w->amount, "\n"; }
        function bump(Wallet $w, int &$count): void { $count = $count + $w->amount; }
        function sum(Wallet $first, int ...$rest): int {
            int $total = $first->amount;
            foreach ($rest as $n) { $total = $total + $n; }
            return $total;
        }
        function makeHost(): Host {
            echo "made\n";
            return new Host();
        }

        function piped(): int { return 5 |> takeWallet(...); }
        function fnWallet(): \Closure { return takeWallet(...); }
        function fnInt(): \Closure { return takeInt(...); }
        function fnMaybe(): \Closure { return takeMaybe(...); }
        function fnScaled(): \Closure { return scaled(...); }
        function fnLog(): \Closure { return logIt(...); }
        function fnBump(): \Closure { return bump(...); }
        function fnSum(): \Closure { return sum(...); }
        function fnHostEat(): \Closure { return makeHost()->eat(...); }
        function fnVarEat(): \Closure { Host $h = new Host(); return $h->eat(...); }
        function fnHostMake(): \Closure { return Host::make(...); }
        function fnFluent(): \Closure { Host $h = new Host(); return $h->fluent(...); }
        function fnPlain(): \Closure { return plain(...); }
        function gen(Wallet $w): \Generator { yield $w->amount; yield $w->amount + 1; }
        function fnGen(): \Closure { return gen(...); }
        function identity<T>(Wallet $w, T $value): T { return $value; }
        function fnIdentity(): \Closure { return identity(...); }
        function plain(string $s): string { return $s; }

        class Host {
            public function eat(Wallet $w): int { return $w->amount + 100; }
            public static function make(Wallet $w): int { return $w->amount + 200; }
            private function secret(Wallet $w): int { return $w->amount + 300; }
            public function viaThis(): \Closure { return $this->secret(...); }
            public static function viaStatic(): \Closure { return static::make(...); }
            public static function viaSelf(): \Closure { return self::make(...); }
            public function fluent(Wallet $w): static { return $this; }
        }
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
    public void Run_FunctionCallable_ConvertsEachSourceType()
    {
        var (php, output, errors) = Run(
            @"$a = \Probe\fnWallet(); echo $a(5), ',', $a('abc'), ',', $a(new \Probe\Wallet()), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("5,3,0\n", php);
        php.Should().Contain("static fn(");
    }

    [Fact]
    public void Run_ConvertToScalar_AcceptsTheSourceClass()
    {
        var (php, output, errors) = Run(
            @"$a = \Probe\fnInt(); echo $a(4), ',', $a(new \Probe\Money()), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("4,9\n", php);
    }

    [Fact]
    public void Run_NullableParameter_PassesNullThrough()
    {
        var (php, output, errors) = Run(
            @"$a = \Probe\fnMaybe(); echo $a(null), ',', $a(6), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("-1,6\n", php);
    }

    [Fact]
    public void Run_DefaultParameter_KeepsItsDefault()
    {
        var (php, output, errors) = Run(
            @"$a = \Probe\fnScaled(); echo $a(5), ',', $a(5, 3), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("10,15\n", php);
    }

    [Fact]
    public void Run_VoidCallee_UsesAFullClosure()
    {
        var (php, output, errors) = Run(@"$a = \Probe\fnLog(); $a(7);");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("log:7\n", php);
        php.Should().Contain(": void");
    }

    [Fact]
    public void Run_ByReferenceParameter_StaysAReference()
    {
        var (php, output, errors) = Run(@"$a = \Probe\fnBump(); $count = 1; $a(4, $count); echo $count, ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("5\n", php);
    }

    [Fact]
    public void Run_VariadicParameter_IsForwarded()
    {
        var (php, output, errors) = Run(
            @"$a = \Probe\fnSum(); echo $a(5, 1, 2), ',', $a('ab', 10), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("8,12\n", php);
    }

    [Fact]
    public void Run_NonVariableReceiver_IsEvaluatedOnceWhenTheCallableIsCreated()
    {
        var (php, output, errors) = Run(
            @"$f = \Probe\fnHostEat(); echo ""created\n""; echo $f(1), ',', $f(2), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("made\ncreated\n101,102\n", php);
    }

    [Fact]
    public void Run_VariableReceiver_IsCapturedWhenTheCallableIsCreated()
    {
        var (php, output, errors) = Run(@"$f = \Probe\fnVarEat(); echo $f(1), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("101\n", php);
        php.Should().Contain("=> $h->eat(");
    }

    [Fact]
    public void Run_StaticMethodCallable_Converts()
    {
        var (php, output, errors) = Run(@"$f = \Probe\fnHostMake(); echo $f(1), ',', $f('ab'), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("201,202\n", php);
    }

    [Fact]
    public void Run_InClassCallables_ReachPrivateAndLateStaticTargets()
    {
        var (php, output, errors) = Run(
            @"$h = new \Probe\Host(); echo $h->viaThis()(1), ',', \Probe\Host::viaStatic()(2), ',', \Probe\Host::viaSelf()(3), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("301,202,203\n", php);
    }

    [Fact]
    public void Run_StaticReturnType_IsNotCopiedOntoTheWrapper()
    {
        var (php, output, errors) = Run(@"$f = \Probe\fnFluent(); echo \get_class($f(3)), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("Probe\\Host\n", php);
    }

    [Fact]
    public void Run_GeneratorCallee_WrapperDeclaresNoReturnType()
    {
        var (php, output, errors) = Run(@"$f = \Probe\fnGen(); echo \implode(',', \iterator_to_array($f(4))), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("4,5\n", php);
    }

    [Fact]
    public void Emit_GenericCallee_WrapsTheNativeCallableThroughGenericWrapCallable()
    {
        var result = SnippetEmit.Compile(Library);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("\\Tyhp\\Generic::wrapCallable(");
        result.Php.Should().Contain("identity(...)");
        result.Php.Should().Contain("$__fccTarget(");
    }

    [Fact]
    public void Run_GenericCallee_ConvertsTheWidenedParameterAndForwardsTheRest()
    {
        var (php, output, errors) = Run(@"$f = \Probe\fnIdentity(); echo $f(7, 'x'), ',', $f(new \Probe\Wallet(), 'y'), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("x,y\n", php);
    }

    [Fact]
    public void Run_PipeIntoWrappedCallable_ConvertsThePipedValue()
    {
        var (php, output, errors) = Run(@"echo \Probe\piped(), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("5\n", php);
    }

    [Fact]
    public void Run_PhpCallers_CanUseTheWrapper()
    {
        var (php, output, errors) = Run(@"echo \implode(',', \array_map(\Probe\fnWallet(), [1, 2, 3])), ""\n"";");

        errors.Should().BeEmpty(string.Join("; ", errors));
        output.Should().Be("1,2,3\n", php);
    }

    [Fact]
    public void Emit_CallableWithoutAConvertibleParameter_StaysNative()
    {
        var result = SnippetEmit.Compile(Library);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("plain(...)");
    }
}
