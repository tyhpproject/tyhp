using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story27")]
public class ObjectShapeGuardCheckerTests
{
    [Fact]
    public void ShapeTypedReceiver_InstanceMethod_IsCallable()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function formatNow(ClockShape $c): int {
                return $c->now();
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void IsGuard_NarrowsObjectToShape_AndAllowsInstanceMethod()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function fromObject(object $obj): int {
                if ($obj is ClockShape) {
                    return $obj->now();
                }
                return 0;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void InstanceofGuard_NarrowsTheSameAsIs()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function fromObject(object $obj): int {
                if ($obj instanceof ClockShape) {
                    return $obj->now();
                }
                return 0;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ShapeTypedReceiver_ConstructIsNotCallable_Reports4359()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function demo(Named $c): void {
                $c->__construct("x");
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeConstructNotCallable);
    }

    [Fact]
    public void AfterIsGuard_ConstructIsNotCallable_Reports4359()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function demo(object $obj): void {
                if ($obj is Named) {
                    $obj->__construct("x");
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeConstructNotCallable);
    }

    [Fact]
    public void NewClassNameOfNewShape_InstanceMethod_IsCallable()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            class Greeter {
                public function __construct(string $name): void {}
                public function ping(): void {}
            }
            function demo(__ClassName<__New<Named>> $cls): void {
                (new $cls("x"))->ping();
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NewConstraintValue_InstanceMethod_IsCallable()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function demo(__New<Named> $c): void {
                $c->ping();
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
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
