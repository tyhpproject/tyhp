using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story27")]
public class NewConstraintCheckerTests
{
    [Fact]
    public void NewInt_Reports4351()
    {
        var errors = Check("""
            <?tyhp
            function demo(__New<int> $x): void {}
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewTypeArgumentNotObjectShape);
    }

    [Fact]
    public void NewNamedClass_Reports4351()
    {
        var errors = Check("""
            <?tyhp
            class User {}
            function demo(__New<User> $x): void {}
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewTypeArgumentNotObjectShape);
    }

    [Fact]
    public void NewBuiltinObject_Reports4351()
    {
        var errors = Check("""
            <?tyhp
            function demo(__New<object> $x): void {}
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewTypeArgumentNotObjectShape);
    }

    [Fact]
    public void NewZeroArg_AcceptsNoCtorClass()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            class Blank {
                public function ping(): void {}
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                Blank $e = make<Blank>();
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NewZeroArg_RejectsAbstract()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            abstract class Abs {
                public function ping(): void {}
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                make<Abs>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintNotConstructable);
    }

    [Fact]
    public void NewZeroArg_RejectsInterface()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            interface Pingable {
                public function ping(): void;
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                make<Pingable>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintNotConstructable);
    }

    [Fact]
    public void NewZeroArg_RejectsEnum()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            enum Status {
                case A;
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                make<Status>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintNotConstructable);
    }

    [Fact]
    public void NewZeroArg_RejectsTrait()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            trait Pingable {
                public function ping(): void {}
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                make<Pingable>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintNotConstructable);
    }

    [Fact]
    public void NewZeroArg_RejectsPrivateConstructor()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            class Hidden {
                private function __construct(): void {}
                public function ping(): void {}
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                make<Hidden>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintNonPublicConstructor);
    }

    [Fact]
    public void NewZeroArg_RejectsRequiredArgConstructor()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            class NeedsDsn {
                public function __construct(string $dsn): void {}
                public function ping(): void {}
            }
            function make<T extends __New<ZeroArg>>(): T {
                return new T();
            }
            function demo(): void {
                make<NeedsDsn>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintConstructorMismatch);
    }

    [Fact]
    public void NewT_WithoutNewBound_Reports4355()
    {
        var errors = Check("""
            <?tyhp
            type Shape = object {
                public function ping(): void;
            };
            function make<T extends Shape>(): T {
                return new T();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewTypeParameterRequiresNew);
    }

    [Fact]
    public void WrittenOptionalCtor_AcceptsOneAndTwoArgNew()
    {
        var errors = Check("""
            <?tyhp
            type PairCtor = object {
                public function __construct(string $a, int $b = 0): void;
            };
            class Pair {
                public function __construct(string $a, int $b = 0): void {}
            }
            function make<T extends __New<PairCtor>>(string $s, int $i): T {
                T $one = new T($s);
                return new T($s, $i);
            }
            function demo(): void {
                Pair $p = make<Pair>('x', 1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void WrittenSecondArg_RejectsClassThatOnlyTakesOne()
    {
        var errors = Check("""
            <?tyhp
            type PairCtor = object {
                public function __construct(string $a, int $b = 0): void;
            };
            class OneArg {
                public function __construct(string $a): void {}
            }
            function make<T extends __New<PairCtor>>(string $s): T {
                return new T($s);
            }
            function demo(): void {
                make<OneArg>('x');
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintConstructorMismatch);
    }

    [Fact]
    public void HasQuery_InstanceAssigns_NewDoesNot()
    {
        var errors = Check("""
            <?tyhp
            type HasQuery = object {
                public function query(string $sql): mixed;
            };
            class PdoLike {
                public function __construct(string $dsn): void {}
                public function query(string $sql): mixed { return null; }
            }
            function take(HasQuery $q): mixed {
                return $q->query('select 1');
            }
            function make<T extends __New<HasQuery>>(): T {
                return new T();
            }
            function demo(PdoLike $pdo): void {
                take($pdo);
                make<PdoLike>();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewConstraintConstructorMismatch);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void NewT_NamedArguments_MatchShapeConstructorRegardlessOfOrder()
    {
        // `new T(...)` argument checking must match named arguments against the shape
        // constructor's parameter names, the same way a nominal `new Foo(...)` does — not
        // just count positional arguments (FOUND: named args were silently skipped, which
        // both false-rejected valid calls and let mistyped named args through unchecked).
        var errors = Check("""
            <?tyhp
            type PairCtor = object {
                public function __construct(string $a, int $b = 0): void;
            };
            class Pair {
                public function __construct(string $a, int $b = 0): void {}
            }
            function make<T extends __New<PairCtor>>(string $s): T {
                return new T(b: 1, a: $s);
            }
            function demo(): void {
                make<Pair>('x');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NewT_NamedArgumentWrongType_Reports4357()
    {
        var errors = Check("""
            <?tyhp
            type PairCtor = object {
                public function __construct(string $a, int $b = 0): void;
            };
            class Pair {
                public function __construct(string $a, int $b = 0): void {}
            }
            function make<T extends __New<PairCtor>>(): T {
                return new T(a: 5);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeConstructorArgumentMismatch);
    }

    [Fact]
    public void NewT_WrongArgs_Reports4357()
    {
        var errors = Check("""
            <?tyhp
            type ZeroArg = object {
                public function ping(): void;
            };
            function make<T extends __New<ZeroArg>>(string $s): T {
                return new T($s);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeConstructorArgumentMismatch);
    }

    private static IReadOnlyList<IDiagnostic> Check(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
