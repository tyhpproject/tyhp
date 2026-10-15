using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// The value of a first-class callable accepts what its emitted wrapper accepts: each parameter
/// that has types converting to it is widened by them. Declared callable types, plain closures
/// and callables of other shapes keep their declared parameters.
/// </summary>
[Trait("Category", "Checker")]
public class CallableWidenedTypeTests
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
            operator convert(string $value): self { return new Wallet(); }
        }

        class Money {
            public int $cents = 9;
            operator convert(self $value): int => $value->cents;
        }

        function takeWallet(Wallet $w): int { return $w->amount; }
        function takeInt(int $n): int { return $n; }
        function takeMaybe(?Wallet $w): int { return 0; }
        function takeString(string $s): string { return $s; }
        function takeRefWallet(Wallet &$w): void {}

        class Host {
            public function eat(Wallet $w): int { return $w->amount; }
            public static function make(Wallet $w): int { return $w->amount; }
        }
        """;

    private static IReadOnlyList<string> Errors(string body) =>
        SnippetEmit.Compile(Library + "\n" + body).Errors;

    private static void AssertCompiles(string body)
    {
        var errors = Errors(body);
        errors.Should().BeEmpty(string.Join("; ", errors));
    }

    [Fact]
    public void FunctionCallable_AcceptsEachConvertibleSource()
    {
        AssertCompiles("""
            function run(): int {
                $f = takeWallet(...);
                return $f(5) + $f('x') + $f(new Wallet());
            }
            """);
    }

    [Fact]
    public void FunctionCallable_StillRejectsATypeWithNoConvert()
    {
        var errors = Errors("""
            function run(): int {
                $f = takeWallet(...);
                return $f(1.5);
            }
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ConvertToScalar_WidensTheScalarParameter()
    {
        AssertCompiles("""
            function run(): int {
                $f = takeInt(...);
                return $f(4) + $f(new Money());
            }
            """);
    }

    [Fact]
    public void NullableParameter_AcceptsNullAndConvertibleSources()
    {
        AssertCompiles("""
            function run(): int {
                $f = takeMaybe(...);
                return $f(null) + $f(3) + $f(new Wallet());
            }
            """);
    }

    [Fact]
    public void MethodCallables_AcceptConvertibleSources()
    {
        AssertCompiles("""
            function run(Host $h): int {
                $eat = $h->eat(...);
                $make = Host::make(...);
                return $eat(1) + $make('a');
            }
            """);
    }

    [Fact]
    public void ByReferenceParameter_IsNotWidened()
    {
        var errors = Errors("""
            function run(): void {
                $f = takeRefWallet(...);
                int $n = 0;
                $f($n);
            }
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void CallableWithoutAConvertibleParameter_KeepsItsSignature()
    {
        var errors = Errors("""
            function run(): string {
                $f = takeString(...);
                return $f(5);
            }
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void WidenedCallable_IsAssignableToTheDeclaredCallableType()
    {
        AssertCompiles("""
            function run(): int {
                callable(Wallet): int $f = takeWallet(...);
                return $f(new Wallet());
            }
            """);
    }

    [Fact]
    public void DeclaredCallableType_DoesNotWiden()
    {
        var errors = Errors("""
            function run(callable(Wallet): int $f): int {
                return $f(5);
            }
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void PlainClosure_DoesNotWiden()
    {
        var errors = Errors("""
            function run(): int {
                $f = fn(Wallet $w): int => $w->amount;
                return $f(5);
            }
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void CallableOfAnotherFile_WidensOnlyWhereTheExtensionApplies()
    {
        var files = new Dictionary<string, string>
        {
            ["Lib.tyhp"] = """
                <?tyhp
                namespace Lib;

                class Cup { public int $ml = 0; }

                extension CupConv extends Cup {
                    operator convert(int $value): self {
                        Cup $c = new Cup();
                        $c->ml = $value;
                        return $c;
                    }
                }

                function takeCup(Cup $c): int { return $c->ml; }
                """,
            ["Without.tyhp"] = """
                <?tyhp
                namespace Without;

                use function Lib\takeCup;

                function run(): int {
                    $f = takeCup(...);
                    return $f(5);
                }
                """,
            ["With.tyhp"] = """
                <?tyhp
                namespace With;

                use extension \Lib\CupConv;
                use function Lib\takeCup;

                function run(): int {
                    $f = takeCup(...);
                    return $f(5);
                }
                """,
        };

        var errors = SnippetEmit.Compile(files).Errors;

        errors.Should().ContainSingle(e => e.StartsWith("4010:"), string.Join("; ", errors));
    }
}
