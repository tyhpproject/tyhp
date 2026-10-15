using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>.tyhp</c> overload sets: bodyless signatures followed directly by one declaration. Callers see
/// only the signatures; the first signature that accepts the call is selected.
/// </summary>
[Trait("Category", "Checker")]
public class SourceOverloadTests
{
    private const string Prelude = """
        <?tyhp
        namespace Probe;

        use Tyhp\Promise;

        final class User {
            public function __construct(public string $name) {}
        }
        """;

    private static void AssertCompiles(string body)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    private static void AssertRejected(string body, string containing)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().Contain(e => e.Contains(containing),
            "errors were: " + string.Join("; ", result.Errors));
    }

    private static void AssertRejectedWithCode(string body, int code)
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + body);
        result.Errors.Should().Contain(e => e.StartsWith(code + ":"),
            "errors were: " + string.Join("; ", result.Errors));
    }

    private const string AreaFunctions = """
        function area(int $r): float;
        function area(int $w, int $h): float;
        function area(int $a, int $b = 0): float {
            return 1.0;
        }
        """;

    // ---- functions: arity and order ----

    [Fact]
    public void Function_CallsMatchingAnyOverload_Compile()
    {
        AssertCompiles(AreaFunctions + """

            function t(): void {
                float $a = area(1);
                float $b = area(1, 2);
            }
            """);
    }

    [Fact]
    public void Function_NoArguments_ReportsNoMatchWithCandidates()
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + AreaFunctions + """

            function t(): void {
                float $a = area();
            }
            """);
        result.Errors.Should().Contain(e => e.StartsWith("4374:") && e.Contains("area(int $r): float") && e.Contains("area(int $w, int $h): float"),
            "errors were: " + string.Join("; ", result.Errors));
    }

    [Fact]
    public void Function_TooManyArguments_ReportsNoMatch()
    {
        AssertRejectedWithCode(AreaFunctions + """

            function t(): void {
                float $a = area(1, 2, 3);
            }
            """, 4374);
    }

    [Fact]
    public void Function_WrongArgumentType_ReportsNoMatch()
    {
        AssertRejectedWithCode(AreaFunctions + """

            function t(): void {
                float $a = area('x');
            }
            """, 4374);
    }

    [Fact]
    public void Function_ImplementationSignatureIsNotCallable()
    {
        // The implementation accepts `string|int`, but only the overloads are visible to callers.
        AssertRejectedWithCode("""
            function pick(int $x): int;
            function pick(string $x, int $y): string;
            function pick(int|string $x, int $y = 0): int|string {
                return $x;
            }

            function t(): void {
                string $s = pick('a');
            }
            """, 4374);
    }

    [Fact]
    public void Function_FirstApplicableOverloadWins_ByDeclarationOrder()
    {
        AssertCompiles("""
            function conv(int $x): string;
            function conv(mixed $x): int;
            function conv(mixed $x): string|int {
                return 1;
            }

            function t(): void {
                string $a = conv(1);
                int $b = conv('a');
            }
            """);
    }

    [Fact]
    public void Function_FirstApplicableOverload_DecidesTheReturnType()
    {
        AssertRejected("""
            function conv(int $x): string;
            function conv(mixed $x): int;
            function conv(mixed $x): string|int {
                return 1;
            }

            function t(): void {
                int $a = conv(1);
            }
            """, "string");
    }

    [Fact]
    public void Function_NamedArguments_SelectByParameterName()
    {
        AssertCompiles("""
            function box(int $count): string;
            function box(string $label, int $count = 1): int;
            function box(int|string $first, int $count = 1): string|int {
                return 1;
            }

            function t(): void {
                string $a = box(count: 3);
                int $b = box(label: 'x');
            }
            """);
    }

    [Fact]
    public void Function_Generics_InferredPerOverload()
    {
        AssertCompiles("""
            function wrap<T>(T $v): array<T>;
            function wrap<T>(T $v, int $n): array<array<T>>;
            function wrap<T>(T $v, int $n = 0): array<T>|array<array<T>> {
                return [$v];
            }

            function t(): void {
                array<int> $a = wrap(1);
                array<array<string>> $b = wrap('x', 2);
            }
            """);
    }

    [Fact]
    public void Function_Variadic_Overload()
    {
        AssertCompiles("""
            function total(int $one): int;
            function total(int ...$many): int;
            function total(int ...$values): int {
                return 0;
            }

            function t(): void {
                int $a = total(1);
                int $b = total(1, 2, 3);
            }
            """);
    }

    // ---- closures ----

    [Fact]
    public void Method_ClosureArguments_SelectOverloadByParameterCountAndAnnotations()
    {
        AssertCompiles("""
            final class Mapper {
                public function map(callable(int): string $f): string;
                public function map(callable(int, int): int $f): int;
                public function map((callable(int): string)|(callable(int, int): int) $f): string|int {
                    return 1;
                }
            }

            function t(Mapper $m): void {
                string $a = $m->map(fn(int $v): string => 'x');
                int $b = $m->map(fn(int $x, int $y): int => $x + $y);
            }
            """);
    }

    [Fact]
    public void Method_UnannotatedClosure_IsCheckedContextuallyAgainstSelectedOverload()
    {
        AssertCompiles("""
            final class Mapper {
                public function map(callable(int): string $f): string;
                public function map(callable(string): int $f, int $extra): int;
                public function map((callable(int): string)|(callable(string): int) $f, int $extra = 0): string|int {
                    return 1;
                }
            }

            function t(Mapper $m): void {
                string $a = $m->map(fn($v) => 'v' . $v);
                int $b = $m->map(fn($s) => \strlen($s), 1);
            }
            """);
    }

    [Fact]
    public void Method_ClosureWithWrongExplicitReturnType_ReportsNoMatch()
    {
        AssertRejectedWithCode("""
            final class Mapper {
                public function map(callable(int): string $f): string;
                public function map(callable(int): int $f, int $x): int;
                public function map((callable(int): string)|(callable(int): int) $f, int $x = 0): string|int {
                    return 1;
                }
            }

            function t(Mapper $m): void {
                string $a = $m->map(fn(int $v): bool => true);
            }
            """, 4374);
    }

    // ---- methods ----

    [Fact]
    public void Method_InstanceOverloads_SelectByArguments()
    {
        AssertCompiles("""
            final class Repo {
                public function find(int $id): User;
                public function find(string $name): ?User;
                public function find(int|string $key): ?User {
                    return null;
                }
            }

            function t(Repo $r): void {
                User $a = $r->find(1);
                ?User $b = $r->find('n');
            }
            """);
    }

    [Fact]
    public void Method_ImplementationSignatureIsNotCallable()
    {
        AssertRejectedWithCode("""
            final class Repo {
                public function find(int $id): User;
                public function find(string $name, bool $strict): ?User;
                public function find(int|string $key, bool $strict = false): ?User {
                    return null;
                }
            }

            function t(Repo $r): void {
                $r->find('n');
            }
            """, 4374);
    }

    [Fact]
    public void Method_StaticOverloads()
    {
        AssertCompiles("""
            final class Ids {
                public static function of(int $id): string;
                public static function of(string $name): int;
                public static function of(int|string $key): string|int {
                    return 1;
                }
            }

            function t(): void {
                string $a = Ids::of(1);
                int $b = Ids::of('n');
            }
            """);
    }

    [Fact]
    public void Method_GenericOverloads_WithClassGenerics()
    {
        AssertCompiles("""
            final class Box<T> {
                public function __construct(public T $value) {}

                public function map<R>(callable(T): R $f): Box<R>;
                public function map<R>(callable(T): R $f, int $times): Box<R>;
                public function map<R>(callable(T): R $f, int $times = 1): Box<R> {
                    return new Box($f($this->value));
                }
            }

            function t(Box<int> $b): void {
                Box<string> $s = $b->map(fn(int $v): string => 'x');
                Box<string> $t = $b->map(fn(int $v): string => 'x', 2);
            }
            """);
    }

    // ---- then-shaped overloads (class generics + method generics + lambdas) ----

    private const string ThenBox = """
        final class Chain<TReturn> {
            public function __construct(public TReturn $value) {}

            public function then(
                null $onFulfilled = null,
                null $onRejected = null
            ): Chain<TReturn>;
            public function then(
                null $onFulfilled,
                callable(\Throwable): TReturn $onRejected
            ): Chain<TReturn>;
            public function then<TThen>(
                callable(TReturn): TThen $onFulfilled,
                ?callable(\Throwable): TThen $onRejected = null
            ): Chain<TThen>;
            public function then<TThen>(
                ?callable(TReturn): TThen $onFulfilled = null,
                null|(callable(\Throwable): TThen)|(callable(\Throwable): TReturn) $onRejected = null
            ): Chain<TThen|TReturn> {
                return new Chain<TThen|TReturn>($this->value);
            }
        }
        """;

    [Fact]
    public void ThenShapedOverloads_NoArguments_ReturnsSameChain()
    {
        AssertCompiles(ThenBox + """

            function t(Chain<int> $c): void {
                Chain<int> $a = $c->then();
                Chain<int> $b = $c->then(null, null);
            }
            """);
    }

    [Fact]
    public void ThenShapedOverloads_RejectionHandlerOnly_KeepsValueType()
    {
        AssertCompiles(ThenBox + """

            function t(Chain<int> $c): void {
                Chain<int> $a = $c->then(null, fn(\Throwable $e): int => 1);
            }
            """);
    }

    [Fact]
    public void ThenShapedOverloads_FulfilmentHandler_InfersHandlerReturn()
    {
        AssertCompiles(ThenBox + """

            function t(Chain<int> $c): void {
                Chain<string> $a = $c->then(fn(int $v): string => 'x');
                Chain<string> $b = $c->then(fn(int $v): string => 'x', fn(\Throwable $e): string => 'y');
            }
            """);
    }

    // ---- async ----

    [Fact]
    public void AsyncMethod_Overloads_ReturnPromiseOfSettledType()
    {
        AssertCompiles("""
            final class Repo {
                async public function load(int $id): User;
                async public function load(string $name): ?User;
                async public function load(int|string $key): ?User {
                    return null;
                }
            }

            async function t(Repo $r): void {
                Promise<User> $a = $r->load(1);
                ?User $b = await $r->load('n');
            }
            """);
    }

    [Fact]
    public void AsyncFunction_Overloads_ReturnPromiseOfSettledType()
    {
        AssertCompiles("""
            async function fetch(int $id): int;
            async function fetch(string $name): string;
            async function fetch(int|string $key): int|string {
                return $key;
            }

            async function t(): void {
                Promise<int> $a = fetch(1);
                string $b = await fetch('n');
            }
            """);
    }

    [Fact]
    public void AsyncOverloadSignature_AgainstSyncImplementation_ReportsModifierMismatch()
    {
        AssertRejectedWithCode("""
            final class Repo {
                async public function load(int $id): User;
                async public function load(string $name): User;
                public function load(int|string $key): User {
                    return new User('x');
                }
            }
            """, 3035);
    }

    // ---- abstract / interface sets ----

    [Fact]
    public void Interface_OverloadSet_AllSignaturesCallable()
    {
        AssertCompiles("""
            interface Store {
                public function get(int $id): User;
                public function get(string $name): ?User;
            }

            function t(Store $s): void {
                User $a = $s->get(1);
                ?User $b = $s->get('n');
            }
            """);
    }

    [Fact]
    public void Interface_OverloadSet_ImplementedByClass()
    {
        AssertCompiles("""
            interface Store {
                public function get(int $id): User|null;
                public function get(string $name): ?User;
            }

            final class MemoryStore implements Store {
                public function get(int|string $key): ?User {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public void Interface_OverloadSet_NoMatchReportsCandidates()
    {
        AssertRejectedWithCode("""
            interface Store {
                public function get(int $id): User;
                public function get(string $name): ?User;
            }

            function t(Store $s): void {
                $s->get(1.5);
            }
            """, 4374);
    }

    [Fact]
    public void AbstractClass_OverloadSet_AllSignaturesCallable()
    {
        AssertCompiles("""
            abstract class Base {
                abstract public function read(int $id): string;
                abstract public function read(string $name): int;
            }

            function t(Base $b): void {
                string $a = $b->read(1);
                int $c = $b->read('n');
            }
            """);
    }

    // ---- declaration diagnostics ----

    [Fact]
    public void Diagnostic_OverloadIncompatibleWithImplementation_Function()
    {
        AssertRejectedWithCode("""
            function size(int $x): string;
            function size(string $x): string;
            function size(int $x): string {
                return 'a';
            }
            """, 4118);
    }

    [Fact]
    public void Diagnostic_OverloadReturnIncompatibleWithImplementation_Method()
    {
        AssertRejectedWithCode("""
            final class Repo {
                public function find(int $id): string;
                public function find(string $name): int;
                public function find(int|string $key): bool {
                    return true;
                }
            }
            """, 4118);
    }

    [Fact]
    public void Diagnostic_OverloadParameterIncompatibleWithImplementation_Method()
    {
        AssertRejectedWithCode("""
            final class Repo {
                public function find(int $id): string;
                public function find(float $name): string;
                public function find(int $key): string {
                    return 'a';
                }
            }
            """, 4118);
    }

    [Fact]
    public void Diagnostic_MissingImplementation_Function()
    {
        AssertRejectedWithCode("""
            function size(int $x): string;
            function size(string $x): string;

            function other(): void {}
            """, 3031);
    }

    [Fact]
    public void Diagnostic_MissingImplementation_Method()
    {
        AssertRejectedWithCode("""
            final class Repo {
                public function find(int $id): string;
                public function find(string $name): string;

                public function other(): void {}
            }
            """, 3031);
    }

    [Fact]
    public void Diagnostic_SignatureAfterImplementation_Function()
    {
        AssertRejectedWithCode("""
            function size(int $x): string;
            function size(int|string $x): string {
                return 'a';
            }
            function size(string $x): string;
            """, 3032);
    }

    [Fact]
    public void Diagnostic_SignatureSeparatedFromImplementation_Method()
    {
        AssertRejectedWithCode("""
            final class Repo {
                public function find(int $id): string;
                public function find(string $name): string;
                public int $count = 0;
                public function find(int|string $key): string {
                    return 'a';
                }
            }
            """, 3032);
    }

    [Fact]
    public void Diagnostic_LoneOverload_IsWarning()
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + """
            function size(int $x): string;
            function size(int|string $x): string {
                return 'a';
            }
            """);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Warnings.Should().Contain(w => w.StartsWith("3033:"), string.Join("; ", result.Warnings));
    }

    [Fact]
    public void Diagnostic_DuplicateSignature()
    {
        AssertRejectedWithCode("""
            function size(int $x): string;
            function size(int $x): string;
            function size(int|string $x): string {
                return 'a';
            }
            """, 3034);
    }

    [Fact]
    public void Diagnostic_ModifierMismatch_StaticNonStatic()
    {
        AssertRejectedWithCode("""
            final class Repo {
                public static function find(int $id): string;
                public function find(string $name): string;
                public static function find(int|string $key): string {
                    return 'a';
                }
            }
            """, 3035);
    }

    [Fact]
    public void Diagnostic_ModifierMismatch_Visibility()
    {
        AssertRejectedWithCode("""
            final class Repo {
                private function find(int $id): string;
                public function find(string $name): string;
                public function find(int|string $key): string {
                    return 'a';
                }
            }
            """, 3035);
    }

    [Fact]
    public void Diagnostic_ConstructorOverloads_NotSupported()
    {
        AssertRejectedWithCode("""
            final class Point {
                public function __construct(int $x);
                public function __construct(string $s);
                public function __construct(int|string $v) {}
            }
            """, 3036);
    }

    [Fact]
    public void OverloadSignature_UnknownParameterType_IsReported()
    {
        var result = SnippetEmit.Compile(Prelude + "\n" + """
            function size(NoSuchType $x): string;
            function size(string $x): string;
            function size(string $x): string {
                return 'a';
            }
            """);
        result.Errors.Should().NotBeEmpty();
    }
}
