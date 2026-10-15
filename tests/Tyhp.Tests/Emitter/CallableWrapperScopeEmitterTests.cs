using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// First-class callables of extension methods, of callees in other namespaces, with a parameter
/// named like the receiver, and with class-relative defaults.
/// </summary>
[Trait("Category", "Emitter")]
public class CallableWrapperScopeEmitterTests
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
        }

        class Money {
            public function __construct(public int $cents = 0) {}
        }

        extension MoneyTools extends Money {
            function plus(int $n): Money { return new Money($this->cents + $n); }
            function plusWallet(Wallet $w): int {
                int $total = $this->cents + $w->amount;
                return $total;
            }
            fn shout(): string => 'cents:' . $this->cents;
        }

        extension IntTools extends int {
            fn twice(): int => $this * 2;
        }

        class Host {
            public const int STEP = 5;
            public function eat(Wallet $w): int { return $w->amount; }
            public function stepped(Wallet $w, int $by = self::STEP): int { return $w->amount * $by; }
        }

        function plusOf(Money $m): \Closure { return $m->plus(...); }
        function plusWalletOf(Money $m): \Closure { return $m->plusWallet(...); }
        function shoutOf(Money $m): \Closure { return $m->shout(...); }
        function twiceOf(int $n): \Closure { return $n->twice(...); }
        function twiceOfCall(): \Closure { return (3 + 4)->twice(...); }
        function collide(Host $w): \Closure { return $w->eat(...); }
        function stepped(Host $h): \Closure { return $h->stepped(...); }
        """;

    private static string RunLibrary(string entry)
    {
        var result = SnippetEmit.Compile(Library);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        return EmittedPhpRunner.Run(result.Files!, entry);
    }

    [Fact]
    public void Run_ExtensionMethodCallable_BindsTheReceiver()
    {
        var output = RunLibrary(@"$p = \Probe\plusOf(new \Probe\Money(10)); echo $p(5)->cents, ""\n"";");

        output.Should().Be("15\n");
    }

    [Fact]
    public void Run_ExtensionMethodCallable_ConvertsItsParameter()
    {
        var output = RunLibrary(@"$p = \Probe\plusWalletOf(new \Probe\Money(10)); echo $p(5), ""\n"";");

        output.Should().Be("15\n");
    }

    [Fact]
    public void Run_ExtensionMethodWithoutParameters_IsCallable()
    {
        var output = RunLibrary(@"$p = \Probe\shoutOf(new \Probe\Money(7)); echo $p(), ""\n"";");

        output.Should().Be("cents:7\n");
    }

    [Fact]
    public void Run_ScalarExtensionMethodCallable_CapturesTheReceiverValue()
    {
        var output = RunLibrary(@"echo \Probe\twiceOf(21)(), ',', \Probe\twiceOfCall()(), ""\n"";");

        output.Should().Be("42,14\n");
    }

    [Fact]
    public void Emit_ExtensionMethodCallable_HasNoUnboundParameter()
    {
        var result = SnippetEmit.Compile(Library);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().NotContain("plus($m, )");
        result.Php.Should().Contain("static fn(int $n): \\Probe\\Money => new Money($m->cents + $n)");
        result.Php.Should().Contain(
            "static fn(\\Probe\\Wallet|int $w): int => \\Probe\\MoneyTools::plusWallet($m, (\\is_int($w) ? \\Probe\\Wallet::__from($w) : $w))");
    }

    [Fact]
    public void Run_ParameterNamedLikeTheReceiver_KeepsBothValues()
    {
        var output = RunLibrary(@"$f = \Probe\collide(new \Probe\Host()); echo $f(8), ""\n"";");

        output.Should().Be("8\n");
    }

    [Fact]
    public void Run_ClassRelativeDefault_IsEvaluatedOutsideTheClass()
    {
        var output = RunLibrary(@"$f = \Probe\stepped(new \Probe\Host()); echo $f(2), ',', $f(2, 3), ""\n"";");

        output.Should().Be("10,6\n");
    }

    [Fact]
    public void Emit_ExtensionConvert_WrapsOnlyWhereTheExtensionApplies()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["Lib.tyhp"] = """
                <?tyhp
                namespace Lib;

                class Wallet { public int $amount = 0; }

                extension WalletConv extends Wallet {
                    operator convert(int $value): self {
                        Wallet $w = new Wallet();
                        $w->amount = $value;
                        return $w;
                    }
                }

                function take(Wallet $w): int { return $w->amount; }
                """,
            ["Without.tyhp"] = """
                <?tyhp
                namespace Without;

                use function Lib\take;

                function fnTake(): \Closure { return take(...); }
                """,
            ["With.tyhp"] = """
                <?tyhp
                namespace With;

                use extension \Lib\WalletConv;
                use function Lib\take;

                function fnTake(): \Closure { return take(...); }
                """,
        });

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        var without = result.Files!.Single(f => f.GeneratedContent!.Contains("namespace Without;")).GeneratedContent!;
        var with = result.Files!.Single(f => f.GeneratedContent!.Contains("namespace With;")).GeneratedContent!;
        without.Should().Contain("take(...)");
        without.Should().NotContain("__from");
        with.Should().Contain("\\Lib\\WalletConv::__from($w)");
        with.Should().NotContain("take(...)");

        var output = EmittedPhpRunner.Run(result.Files!, @"echo \With\fnTake()(6), ""\n"";");
        output.Should().Be("6\n");
    }

    [Fact]
    public void Run_CalleeFromAnotherNamespace_IsWrapped()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["Lib.tyhp"] = """
                <?tyhp
                namespace Lib;

                class Wallet {
                    public int $amount = 0;
                    operator convert(int $value): self {
                        Wallet $w = new Wallet();
                        $w->amount = $value;
                        return $w;
                    }
                }

                function take(Wallet $w): int { return $w->amount; }
                """,
            ["App.tyhp"] = """
                <?tyhp
                namespace App;

                use function Lib\take;

                function fnTake(): \Closure { return take(...); }
                """,
        });

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        var output = EmittedPhpRunner.Run(result.Files!, @"echo \App\fnTake()(6), ""\n"";");

        output.Should().Be("6\n");
    }
}
