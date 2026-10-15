using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// The callable value of a source overload set (<c>foo(...)</c>, <c>$obj-&gt;m(...)</c>,
/// <c>C::m(...)</c>, an overloaded <c>__invoke</c>) has one callable facet per declared signature,
/// in declaration order; the implementation signature is not part of it. Calling the value picks
/// the first accepting facet (TYHP4374 when none does); a callable-typed target needs one facet
/// that satisfies it.
/// </summary>
[Trait("Category", "Checker")]
public class OverloadedCallableValueTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        function pick(int $v): int;
        function pick(string $v): string;
        function pick(int|string $v): int|string {
            return $v;
        }

        function join(string $a): string;
        function join(string $a, string $b = '-'): string;
        function join(string $a, string $b = '-'): string {
            return $a . $b;
        }

        final class Repo {
            public function find(int $id): int;
            public function find(string $name): string;
            public function find(int|string $key): int|string {
                return $key;
            }

            public static function of(int $id): int;
            public static function of(string $name): string;
            public static function of(int|string $key): int|string {
                return $key;
            }
        }

        final class Doubler {
            public function __invoke(int $v): int;
            public function __invoke(string $v): string;
            public function __invoke(int|string $v): int|string {
                return $v;
            }
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

    private static void AssertRejected(string body)
    {
        var errors = Errors(body);
        errors.Should().NotBeEmpty();
    }

    // ---- first-class callable of a function ----

    [Fact]
    public void FunctionReference_CallResolvesToTheMatchingDeclaredSignature()
    {
        AssertCompiles("""
            function t(): void {
                $g = pick(...);
                int $a = $g(1);
                string $b = $g('x');
            }
            """);
    }

    [Fact]
    public void FunctionReference_CallWithNoAcceptingSignature_Reports4374()
    {
        AssertRejectedWithCode("""
            function t(): void {
                $g = pick(...);
                $g(1.5);
            }
            """, 4374);
    }

    [Fact]
    public void FunctionReference_WrongArity_Reports4374()
    {
        AssertRejectedWithCode("""
            function t(): void {
                $g = pick(...);
                $g(1, 2);
            }
            """, 4374);
    }

    [Fact]
    public void FunctionReference_ImplementationReturnTypeIsNotUsed()
    {
        AssertRejectedWithCode("""
            function t(): void {
                $g = pick(...);
                string $s = $g(1);
            }
            """, 4008);
    }

    [Fact]
    public void FunctionReference_AssignsToACallableTypeOneSignatureSatisfies()
    {
        AssertCompiles("""
            function t(): void {
                callable(int): int $a = pick(...);
                callable(string): string $b = pick(...);
            }
            """);
    }

    [Fact]
    public void FunctionReference_DoesNotAssignToACallableTypeNoSignatureSatisfies()
    {
        AssertRejected("""
            function t(): void {
                callable(float): float $a = pick(...);
            }
            """);
    }

    [Fact]
    public void FunctionReference_ImplementationOnlySignatureIsNotCallable()
    {
        AssertRejected("""
            function t(): void {
                callable(int|string): int|string $a = pick(...);
            }
            """);
    }

    [Fact]
    public void FunctionReference_PassedToAParameterOfCallableType()
    {
        AssertCompiles("""
            function apply(callable(string): string $f, string $v): string {
                return $f($v);
            }

            function t(): string {
                return apply(pick(...), 'x');
            }
            """);
    }

    [Fact]
    public void FunctionReference_WithDefaults_FacetsFollowEachDeclaredSignature()
    {
        AssertCompiles("""
            function t(): void {
                $g = join(...);
                string $a = $g('a');
                string $b = $g('a', 'b');
            }
            """);
    }

    // ---- first-class callable of methods ----

    [Fact]
    public void InstanceMethodReference_CallResolvesToTheMatchingDeclaredSignature()
    {
        AssertCompiles("""
            function t(Repo $r): void {
                $g = $r->find(...);
                int $a = $g(1);
                string $b = $g('x');
            }
            """);
    }

    [Fact]
    public void InstanceMethodReference_CallWithNoAcceptingSignature_Reports4374()
    {
        AssertRejectedWithCode("""
            function t(Repo $r): void {
                $g = $r->find(...);
                $g(1.5);
            }
            """, 4374);
    }

    [Fact]
    public void StaticMethodReference_CallResolvesToTheMatchingDeclaredSignature()
    {
        AssertCompiles("""
            function t(): void {
                $g = Repo::of(...);
                int $a = $g(1);
                string $b = $g('x');
            }
            """);
    }

    [Fact]
    public void StaticMethodReference_AssignsToACallableType()
    {
        AssertCompiles("""
            function t(): void {
                callable(string): string $g = Repo::of(...);
            }
            """);
    }

    // ---- objects with an overloaded __invoke ----

    [Fact]
    public void InvokeReference_CallResolvesToTheMatchingDeclaredSignature()
    {
        AssertCompiles("""
            function t(Doubler $d): void {
                $g = $d->__invoke(...);
                int $a = $g(1);
                string $b = $g('x');
            }
            """);
    }

    [Fact]
    public void ObjectCall_ReturnTypeComesFromTheMatchingDeclaredSignature()
    {
        AssertCompiles("""
            function t(Doubler $d): void {
                int $a = $d(1);
                string $b = $d('x');
            }
            """);
    }

    [Fact]
    public void ObjectCall_WithNoAcceptingSignature_Reports4374()
    {
        AssertRejectedWithCode("""
            function t(Doubler $d): void {
                $d(1.5);
            }
            """, 4374);
    }

    [Fact]
    public void ObjectAssignsToACallableTypeOneSignatureSatisfies()
    {
        AssertCompiles("""
            function t(Doubler $d): void {
                callable(int): int $a = $d;
                callable(string): string $b = $d;
            }
            """);
    }

    [Fact]
    public void ObjectDoesNotAssignToACallableTypeNoSignatureSatisfies()
    {
        AssertRejected("""
            function t(Doubler $d): void {
                callable(float): float $a = $d;
            }
            """);
    }

    // ---- plain (non-overloaded) callables are unchanged ----

    [Fact]
    public void PlainFunctionReference_StillTypesFromItsSignature()
    {
        AssertCompiles("""
            function plain(int $v): int {
                return $v;
            }

            function t(): void {
                $g = plain(...);
                int $a = $g(1);
                callable(int): int $b = plain(...);
            }
            """);
    }
}
