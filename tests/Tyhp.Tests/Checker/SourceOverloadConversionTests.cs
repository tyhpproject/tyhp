using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Source overload selection is two-pass: the first declared signature that accepts the call as
/// written wins; only when none does, signatures are tried again allowing the implicit
/// <c>operator convert</c> a plain call applies. A call that needs conversions to different types
/// in more than one signature is ambiguous (TYHP4375).
/// </summary>
[Trait("Category", "Overloads")]
public class SourceOverloadConversionTests
{
    private const string Prelude = """
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

        final class Celsius {
            public function __construct(public int $degrees) {}
            operator convert(self $value): int { return $value->degrees; }
        }
        """;

    private static IReadOnlyList<string> Errors(string body) =>
        SnippetEmit.Compile(Prelude + "\n" + body).Errors;

    private static void AssertCompiles(string body)
    {
        var errors = Errors(body);
        errors.Should().BeEmpty(string.Join("; ", errors));
    }

    private static void AssertRejectedWithCode(string body, int code)
    {
        var errors = Errors(body);
        errors.Should().Contain(e => e.StartsWith(code + ":"), "errors were: " + string.Join("; ", errors));
    }

    // ---- pass 2: a conversion makes a signature accept the call ----

    [Fact]
    public void Function_ConversionOnlyCall_SelectsTheConvertingSignature()
    {
        AssertCompiles("""
            function takeMoney(Money $m): string;
            function takeMoney(string $label, int $cents): string;
            function takeMoney(Money|string $a, int $cents = 0): string {
                return 'x';
            }

            function run(): string {
                return takeMoney(5);
            }
            """);
    }

    [Fact]
    public void Function_ConversionOnlyCall_TypesTheCallFromTheSelectedSignature()
    {
        AssertCompiles("""
            function describe(Money $m): int;
            function describe(string $s): string;
            function describe(Money|string $a): int|string {
                return 1;
            }

            function run(): int {
                int $r = describe(5);
                return $r;
            }
            """);
    }

    [Fact]
    public void Function_ScalarConvertTo_AcceptsTheObjectForAnIntSignature()
    {
        AssertCompiles("""
            function takesInt(int $n): string;
            function takesInt(string $s, int $extra): string;
            function takesInt(int|string $a, int $extra = 0): string {
                return 'x';
            }

            function run(Celsius $c): string {
                return takesInt($c);
            }
            """);
    }

    [Fact]
    public void Method_ConversionOnlyCall_SelectsTheConvertingSignature()
    {
        AssertCompiles("""
            final class Till {
                public function take(Money $m): string;
                public function take(string $label, int $cents): string;
                public function take(Money|string $a, int $cents = 0): string {
                    return 'x';
                }
            }

            function run(Till $t): string {
                return $t->take(5);
            }
            """);
    }

    [Fact]
    public void StaticMethod_ConversionOnlyCall_SelectsTheConvertingSignature()
    {
        AssertCompiles("""
            final class Bank {
                public static function deposit(Money $m): string;
                public static function deposit(string $label, int $cents): string;
                public static function deposit(Money|string $a, int $cents = 0): string {
                    return 'x';
                }
            }

            function run(): string {
                return Bank::deposit(5);
            }
            """);
    }

    [Fact]
    public void StaticMethod_ConversionsToDifferentTypes_AreAmbiguous()
    {
        AssertRejectedWithCode("""
            final class Bank {
                public static function read(Money $m): string;
                public static function read(Meters $m): string;
                public static function read(Money|Meters $a): string {
                    return 'x';
                }
            }

            function run(): string {
                return Bank::read(5);
            }
            """, 4375);
    }

    // ---- exact beats conversion ----

    [Fact]
    public void Function_ExactMatchBeatsAnEarlierConversionOnlySignature()
    {
        AssertCompiles("""
            function size(Money $m): int;
            function size(int $n): string;
            function size(Money|int $a): int|string {
                return 1;
            }

            function run(): string {
                string $s = size(5);
                return $s;
            }
            """);
    }

    // ---- ambiguity ----

    [Fact]
    public void Function_ConversionsToDifferentTypes_AreAmbiguous()
    {
        AssertRejectedWithCode("""
            function measure(Money $m): string;
            function measure(Meters $m): string;
            function measure(Money|Meters $a): string {
                return 'x';
            }

            function run(): string {
                return measure(5);
            }
            """, 4375);
    }

    [Fact]
    public void Function_AmbiguityMessage_ListsTheCandidatesAndNamesTheFix()
    {
        var errors = Errors("""
            function measure(Money $m): string;
            function measure(Meters $m): string;
            function measure(Money|Meters $a): string {
                return 'x';
            }

            function run(): string {
                return measure(5);
            }
            """);

        errors.Should().Contain(e =>
            e.StartsWith("4375:")
            && e.Contains("measure(\\Probe\\Money $m)")
            && e.Contains("measure(\\Probe\\Meters $m)")
            && e.Contains("explicitly"),
            string.Join("; ", errors));
    }

    [Fact]
    public void Function_ConversionsToTheSameType_AreNotAmbiguous()
    {
        AssertCompiles("""
            function charge(Money $m): string;
            function charge(Money $m, int $times): string;
            function charge(Money $m, int $times = 1): string {
                return 'x';
            }

            function run(): string {
                return charge(5);
            }
            """);
    }

    [Fact]
    public void Function_FewerConversionsDominate_SoTheCallIsNotAmbiguous()
    {
        AssertCompiles("""
            function split(Money $a, Money $b): string;
            function split(Money $a, int $b): string;
            function split(Money $a, Money|int $b): string {
                return 'x';
            }

            function run(): string {
                return split(5, 6);
            }
            """);
    }

    [Fact]
    public void Function_ExplicitConversion_ResolvesTheAmbiguity()
    {
        AssertCompiles("""
            function measure(Money $m): string;
            function measure(Meters $m): string;
            function measure(Money|Meters $a): string {
                return 'x';
            }

            function run(): string {
                return measure(new Money(5));
            }
            """);
    }

    [Fact]
    public void Method_ConversionsToDifferentTypes_AreAmbiguous()
    {
        AssertRejectedWithCode("""
            final class Gauge {
                public function read(Money $m): string;
                public function read(Meters $m): string;
                public function read(Money|Meters $a): string {
                    return 'x';
                }
            }

            function run(Gauge $g): string {
                return $g->read(5);
            }
            """, 4375);
    }

    // ---- callable values ----

    [Fact]
    public void Invoke_ConversionOnlyCall_SelectsTheConvertingSignature()
    {
        AssertCompiles("""
            final class Till {
                public function __invoke(Money $m): string;
                public function __invoke(string $label, int $cents): string;
                public function __invoke(Money|string $a, int $cents = 0): string {
                    return 'x';
                }
            }

            function run(Till $t): string {
                return $t(5);
            }
            """);
    }

    [Fact]
    public void Invoke_ConversionsToDifferentTypes_AreAmbiguous()
    {
        AssertRejectedWithCode("""
            final class Gauge {
                public function __invoke(Money $m): string;
                public function __invoke(Meters $m): string;
                public function __invoke(Money|Meters $a): string {
                    return 'x';
                }
            }

            function run(Gauge $g): string {
                return $g(5);
            }
            """, 4375);
    }

    [Fact]
    public void OverloadedCallableValue_AcceptsConvertibleArgument_ThroughItsWrapper()
    {
        // The first-class callable of an overload set is a wrapper that converts per parameter
        // position, so its value accepts the type that converts to the signature's parameter.
        var errors = Errors("""
            function takeMoney(Money $m): string;
            function takeMoney(string $label, int $cents): string;
            function takeMoney(Money|string $a, int $cents = 0): string {
                return 'x';
            }

            function run(): string {
                $fn = takeMoney(...);
                return $fn(5);
            }
            """);

        errors.Should().BeEmpty(string.Join("; ", errors));
    }

    [Fact]
    public void OverloadedCallableValue_StillRejectsAnArgumentNothingConvertsFrom()
    {
        var errors = Errors("""
            function takeMoney(Money $m): string;
            function takeMoney(string $label, int $cents): string;
            function takeMoney(Money|string $a, int $cents = 0): string {
                return 'x';
            }

            function run(): string {
                $fn = takeMoney(...);
                return $fn(1.5);
            }
            """);

        errors.Should().Contain(e => e.StartsWith("4374:"), string.Join("; ", errors));
    }

    // ---- no match ----

    [Fact]
    public void Function_NoSignatureAcceptsEvenWithConversions_StillReports4374()
    {
        AssertRejectedWithCode("""
            function takeMoney(Money $m): string;
            function takeMoney(string $label, int $cents): string;
            function takeMoney(Money|string $a, int $cents = 0): string {
                return 'x';
            }

            function run(): string {
                return takeMoney(1.5);
            }
            """, 4374);
    }
}
