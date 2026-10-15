using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// The call-site rewrite for an extension <c>operator convert</c> applies to every call the checker
/// accepts through it — including a function declared in another namespace or imported with
/// <c>use function</c> — and only in files where the extension applies.
/// </summary>
[Trait("Category", "Emitter")]
public class ExtensionConvertScopeEmitterTests
{
    private const string Library = """
        <?tyhp
        namespace Probe;

        class Wallet { public int $amount = 0; }

        extension WalletConv extends Wallet {
            operator convert(int $value): self {
                Wallet $w = new Wallet();
                $w->amount = $value;
                return $w;
            }
            operator convert(self $value): int => $value->amount;
        }

        function takeWallet(Wallet $w): int {
            return $w->amount;
        }

        function takeInt(int $n): int {
            return $n;
        }
        """;

    private static SnippetEmit.Result Compile(string other) =>
        SnippetEmit.Compile(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = Library,
            ["b.tyhp"] = other,
        });

    [Fact]
    public void QualifiedCallInAnotherNamespace_RewritesConvertFrom()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use extension \Probe\WalletConv;

            function useIt(int $n): int {
                return \Probe\takeWallet($n);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("\\Probe\\takeWallet(\\Probe\\WalletConv::__from($n))");
    }

    [Fact]
    public void UseFunctionImportedCall_RewritesConvertFrom()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use extension \Probe\WalletConv;
            use function Probe\takeWallet;

            function useIt(int $n): int {
                return takeWallet($n);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("\\Probe\\WalletConv::__from($n)");
    }

    [Fact]
    public void QualifiedCallInAnotherNamespace_RewritesConvertTo()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use extension \Probe\WalletConv;
            use Probe\Wallet;

            function useIt(Wallet $w): int {
                return \Probe\takeInt($w);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("\\Probe\\takeInt(\\Probe\\WalletConv::__to");
    }

    [Fact]
    public void ShortSyntaxExtensionConvert_EmitsTheStaticMethodsTheCallSitesCall()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            namespace Probe;

            class Cup { public function __construct(public int $ml = 0) {} }

            extension CupConv extends Cup {
                operator convert(int $value): self => new Cup($value);
                operator convert(self $value): int => $value->ml;
            }

            function takeCup(Cup $c): int { return $c->ml; }
            function takeMl(int $n): int { return $n; }

            function cups(int $n, Cup $c): int {
                return takeCup($n) + takeMl($c);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().Contain("class CupConv");
        result.Php.Should().Contain("public static function __from(int $from): \\Probe\\Cup");
        result.Php.Should().Contain("public static function __toInt(\\Probe\\Cup $o): int");

        var output = EmittedPhpRunner.Run(
            result.Files!, @"echo \Probe\cups(40, new \Probe\Cup(2)), ""\n"";");

        output.Should().Be("42\n");
    }

    [Fact]
    public void QualifiedCallInAnotherNamespace_RunsInPhp()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use extension \Probe\WalletConv;
            use Probe\Wallet;

            function useIt(int $n): int {
                Wallet $w = new Wallet();
                $w->amount = 4;
                return \Probe\takeWallet($n) + \Probe\takeInt($w);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));

        var output = EmittedPhpRunner.Run(result.Files!, @"echo \Other\useIt(38), ""\n"";");

        output.Should().Be("42\n");
    }
}
