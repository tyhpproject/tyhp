using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story27")]
public class ClassNameShapeCheckerTests
{
    [Fact]
    public void ClassNameOfShape_NewOnVariable_Reports4356()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function demo(__ClassName<ClockShape> $n): void {
                mixed $x = new $n();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewClassNameRequiresNew);
    }

    [Fact]
    public void ClassNameOfShape_NewOnVariable_DoesNotCascadeIntoTypeMismatch()
    {
        // `new $n()` on `__ClassName<ClockShape>` is a hard error (TYHP4356): the expression
        // must not also be typed as a successful `ClockShape` construct, which would mask the
        // real diagnostic behind a spurious secondary assignment-mismatch error.
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function demo(__ClassName<ClockShape> $n): void {
                string $x = new $n();
            }
            """);

        errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerNewClassNameRequiresNew);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void ClassNameOfNewShape_AllowsMatchingConstructorArgs()
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
            function spinUp(__ClassName<__New<Named>> $cls, string $name): Named {
                return new $cls($name);
            }
            function demo(): void {
                Named $obj = spinUp(Greeter::class, 'x');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ClassNameOfNewShape_WrongArgType_Reports4357()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function spinUp(__ClassName<__New<Named>> $cls): Named {
                return new $cls(1);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeConstructorArgumentMismatch);
    }

    [Fact]
    public void ClassNameOfNewShape_MissingArg_Reports4357()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function spinUp(__ClassName<__New<Named>> $cls): Named {
                return new $cls();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeConstructorArgumentMismatch);
    }

    [Fact]
    public void ClassNameOfNewShape_NamedArguments_MatchShapeConstructor()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function spinUp(__ClassName<__New<Named>> $cls, string $name): Named {
                return new $cls(name: $name);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ClassExistsOfShape_NarrowsToClassNameOfShape()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function demo(string $s): void {
                if (\class_exists<ClockShape>($s)) {
                    __ClassName<ClockShape> $typed = $s;
                    mixed $x = new $s();
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerNewClassNameRequiresNew);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void ClassExistsOfNewShape_NarrowsAndAllowsNew()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            function demo(string $s): void {
                if (\class_exists<__New<Named>>($s)) {
                    Named $obj = new $s('x');
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void LiteralMatchingClass_AssignsToClassNameOfShapeWithoutGuard()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            class WallClock {
                public function now(): int { return 1; }
            }
            function demo(): void {
                __ClassName<ClockShape> $n = 'WallClock';
                __ClassName<ClockShape> $fromClass = WallClock::class;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void LiteralNonMatchingClass_DoesNotAssignToClassNameOfShape()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            class NotAClock {
                public function later(): int { return 1; }
            }
            function demo(): void {
                __ClassName<ClockShape> $n = 'NotAClock';
            }
            """);

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void LiteralConstructableClass_AssignsToClassNameOfNewShape()
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
            function demo(): void {
                __ClassName<__New<Named>> $n = 'Greeter';
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void RequiredArgClass_AssignsToClassNameOfShape_NotToNew()
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
            function demo(): void {
                __ClassName<HasQuery> $ok = 'PdoLike';
                __ClassName<__New<HasQuery>> $no = 'PdoLike';
            }
            """);

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            || d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerNewConstraintConstructorMismatch);
        errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTypeMismatch
            && (d.Message ?? "").Contains("__ClassName<HasQuery>", StringComparison.Ordinal));
    }

    [Fact]
    public void AbstractClass_AssignsToClassNameOfShape_NotToNew()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            abstract class AbsClock {
                public function now(): int { return 1; }
            }
            function demo(): void {
                __ClassName<ClockShape> $ok = 'AbsClock';
                __ClassName<__New<ClockShape>> $no = 'AbsClock';
            }
            """);

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            || d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerNewConstraintNotConstructable);
    }

    [Fact]
    public void ClassNameOfMatchingNominal_AssignsToClassNameOfShape()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            class WallClock {
                public function now(): int { return 1; }
            }
            function demo(__ClassName<WallClock> $n): void {
                __ClassName<ClockShape> $s = $n;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ClassNameOfShape_DoesNotAssignToNominalClassName()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            class WallClock {
                public function now(): int { return 1; }
            }
            function demo(__ClassName<ClockShape> $s): void {
                __ClassName<WallClock> $n = $s;
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void ClassNameObject_StillAllowsDynamicNew()
    {
        var errors = Check("""
            <?tyhp
            function demo(__ClassName<object> $n): void {
                mixed $x = new $n();
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerNewClassNameRequiresNew);
    }

    [Fact]
    public void DynamicString_StillNeedsClassExistsGuard()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function demo(string $s): void {
                __ClassName<ClockShape> $n = $s;
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
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
