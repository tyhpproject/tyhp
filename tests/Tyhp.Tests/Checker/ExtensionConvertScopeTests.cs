using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A project-source extension's <c>operator convert</c> applies only in the file that declares the
/// extension, in a file that imports it with <c>use extension</c>, and in every file once a file
/// declares it with <c>global use extension</c>. A class-owned convert always applies.
/// </summary>
[Trait("Category", "Checker")]
public class ExtensionConvertScopeTests
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

        function sameFile(int $n, Wallet $w): int {
            return takeWallet($n) + takeInt($w);
        }
        """;

    private static SnippetEmit.Result Compile(string other) =>
        SnippetEmit.Compile(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = Library,
            ["b.tyhp"] = other,
        });

    [Fact]
    public void Convert_InDeclaringFile_Applies()
    {
        var result = SnippetEmit.Compile(Library);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    [Fact]
    public void ConvertFrom_InOtherFileWithoutImport_IsAnArgumentError()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            function useIt(int $n): int {
                return \Probe\takeWallet($n);
            }
            """);

        result.Errors.Should().ContainSingle(e => e.StartsWith("4010:", StringComparison.Ordinal),
            string.Join("; ", result.Errors));
    }

    [Fact]
    public void ConvertTo_InOtherFileWithoutImport_IsAnArgumentError()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use Probe\Wallet;

            function useIt(Wallet $w): int {
                return \Probe\takeInt($w);
            }
            """);

        result.Errors.Should().ContainSingle(e => e.StartsWith("4010:", StringComparison.Ordinal),
            string.Join("; ", result.Errors));
    }

    [Fact]
    public void ConvertTo_InOtherFileWithoutImport_IsAReturnError()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use Probe\Wallet;

            function useIt(Wallet $w): int {
                return $w;
            }
            """);

        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Convert_InOtherFileWithUseExtension_Applies()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use extension \Probe\WalletConv;
            use Probe\Wallet;

            function useIt(int $n, Wallet $w): int {
                return \Probe\takeWallet($n) + \Probe\takeInt($w);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    [Fact]
    public void Convert_InOtherFileWithGlobalUseExtension_Applies()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = Library,
            ["c.tyhp"] = """
                <?tyhp
                namespace Config;

                global use extension \Probe\WalletConv;
                """,
            ["b.tyhp"] = """
                <?tyhp
                namespace Other;

                function useIt(int $n): int {
                    return \Probe\takeWallet($n);
                }
                """,
        });

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    [Fact]
    public void Convert_FromAClassBody_AppliesInEveryFile()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = """
                <?tyhp
                namespace Probe;

                class Money {
                    public function __construct(public int $cents = 0) {}
                    operator convert(int $value) {
                        return new Money($value);
                    }
                }

                function takeMoney(Money $m): int {
                    return $m->cents;
                }
                """,
            ["b.tyhp"] = """
                <?tyhp
                namespace Other;

                function useIt(int $n): int {
                    return \Probe\takeMoney($n);
                }
                """,
        });

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }
}
