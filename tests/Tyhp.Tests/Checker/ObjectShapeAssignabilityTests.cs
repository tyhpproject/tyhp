using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story27")]
public class ObjectShapeAssignabilityTests
{
    [Fact]
    public void ClassWithExtraMethods_AssignsToSmallerShape()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            class WallClock {
                public function now(): int { return 1; }
                public function extra(): void {}
            }
            function take(Clock $c): void {}
            function give(WallClock $w): void {
                take($w);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void MissingMethod_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            class NotAClock {
                public function later(): int { return 1; }
            }
            function take(Clock $c): void {}
            function give(NotAClock $w): void {
                take($w);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void IncompatibleSignature_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            class StringClock {
                public function now(): string { return "x"; }
            }
            function take(Clock $c): void {}
            function give(StringClock $w): void {
                take($w);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ProtectedMethod_DoesNotSatisfyPublicShapeMethod()
    {
        var errors = Check("""
            <?tyhp
            type NeedsFoo = object {
                public function foo(): void;
            };
            class Hidden {
                protected function foo(): void {}
            }
            function take(NeedsFoo $x): void {}
            function give(Hidden $h): void {
                take($h);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void MagicCall_DoesNotSatisfyNamedMethod()
    {
        var errors = Check("""
            <?tyhp
            type NeedsFoo = object {
                public function foo(): void;
            };
            class Magic {
                public function __call(string $name, array $args): mixed {
                    return null;
                }
            }
            function take(NeedsFoo $x): void {}
            function give(Magic $m): void {
                take($m);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ExplicitCall_OnBothSides_Matches()
    {
        var errors = Check("""
            <?tyhp
            type Caller = object {
                public function __call(string $name, array $args): mixed;
            };
            class Magic {
                public function __call(string $name, array $args): mixed {
                    return null;
                }
            }
            function take(Caller $x): void {}
            function give(Magic $m): void {
                take($m);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Object_DoesNotAssignToShape_Reports4358()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            function take(Clock $c): void {}
            function fromObject(object $o): void {
                take($o);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeRequiresGuard);
    }

    [Fact]
    public void Mixed_DoesNotAssignToShape_Reports4358()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            function take(Clock $c): void {}
            function fromMixed(mixed $m): void {
                take($m);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeRequiresGuard);
    }

    [Fact]
    public void DateTimeImmutable_AssignsToPublicApiShape()
    {
        var errors = Check(
            """
            <?tyhp
            type HasDiff = object {
                public function diff(\DateTimeInterface $targetObject, bool $absolute = false): \DateInterval;
            };
            function take(HasDiff $d): void {}
            function give(\DateTimeImmutable $dt): void {
                take($dt);
            }
            """,
            SyntheticPhpStubs.DateTimeOperators);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ConstructOnShape_IsIgnoredForInstanceAssignability()
    {
        var errors = Check("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            class Pinger {
                public function ping(): void {}
            }
            function take(Named $n): void {}
            function give(Pinger $p): void {
                take($p);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtraDefaultedParameterOnClass_AssignsToSmallerShapeMethod()
    {
        var errors = Check("""
            <?tyhp
            type Ping = object {
                public function ping(): void;
            };
            class Pinger {
                public function ping(string $extra = ""): void {}
            }
            function take(Ping $p): void {}
            function give(Pinger $x): void {
                take($x);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void GenericShape_SubstitutesMemberSignatures()
    {
        var errors = Check("""
            <?tyhp
            type Box<T> = object {
                public function get(): T;
            };
            class IntBox {
                public function get(): int { return 1; }
            }
            function take(Box<int> $b): void {}
            function give(IntBox $x): void {
                take($x);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void GenericShape_RejectsIncompatibleTypeArgument()
    {
        var errors = Check("""
            <?tyhp
            type Box<T> = object {
                public function get(): T;
            };
            class StringBox {
                public function get(): string { return "x"; }
            }
            function take(Box<int> $b): void {}
            function give(StringBox $x): void {
                take($x);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void RecursiveShape_DoesNotInfiniteLoop()
    {
        var errors = Check("""
            <?tyhp
            type Node = object {
                public function parent(): ?Node;
            };
            class TreeNode {
                public function parent(): ?TreeNode {
                    return null;
                }
            }
            function take(Node $n): void {}
            function give(TreeNode $t): void {
                take($t);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void TwoShapesWithSameMembers_AreStructurallyEqual()
    {
        var errors = Check("""
            <?tyhp
            type Alpha = object {
                public function ping(): void;
            };
            type Beta = object {
                public function ping(): void;
            };
            function take(Alpha $a): void {}
            function give(Beta $b): void {
                take($b);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void IntersectionShape_RequiresNominalAndStructural()
    {
        var errors = Check("""
            <?tyhp
            interface Logger {
                public function info(string $m): void;
            }
            type TimestampedLogger = Logger & object {
                public function getLastLogAt(): int;
            };
            class AppLogger implements Logger {
                public function info(string $m): void {}
                public function getLastLogAt(): int { return 0; }
            }
            class OnlyLogger implements Logger {
                public function info(string $m): void {}
            }
            function take(TimestampedLogger $l): void {}
            function ok(AppLogger $a): void {
                take($a);
            }
            function bad(OnlyLogger $l): void {
                take($l);
            }
            """);

        errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Shape_AssignsToObject()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            function take(object $o): void {}
            function give(Clock $c): void {
                take($c);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void GetterOnlyShapeProperty_MatchesReadWriteClassProperty()
    {
        var errors = Check("""
            <?tyhp
            type HasName = object {
                public readonly string $name;
            };
            class Person {
                public string $name = "";
            }
            function take(HasName $h): void {}
            function give(Person $p): void {
                take($p);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void WritableShapeProperty_DoesNotMatchReadonlyClassProperty()
    {
        var errors = Check("""
            <?tyhp
            type MutableName = object {
                public string $name;
            };
            class Person {
                public readonly string $name = "";
            }
            function take(MutableName $h): void {}
            function give(Person $p): void {
                take($p);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void UnionSource_AssignsToShape_OnlyWhenEveryArmDoes()
    {
        var errors = Check("""
            <?tyhp
            type Clock = object {
                public function now(): int;
            };
            class WallClock {
                public function now(): int { return 1; }
            }
            function take(Clock $c): void {}
            function give(WallClock|int $w): void {
                take($w);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void NestedShapeMember_ClassChainAssigns()
    {
        // Regression: a shape member type that names a *different* shape alias
        // (`Person::address(): Address`) used to stay an unexpanded, member-less
        // `ObjectShapeCheckedType` (only the top-level parameter annotation ran through
        // `ExpandTypeAliases`), so `RealPerson` structurally satisfying `Person` failed even
        // though `RealAddress` structurally satisfies `Address`.
        var errors = Check("""
            <?tyhp
            type Address = object {
                public function city(): string;
            };
            type Person = object {
                public function address(): Address;
            };
            class RealAddress {
                public function city(): string { return "x"; }
            }
            class RealPerson {
                public function address(): RealAddress { return new RealAddress(); }
            }
            function take(Person $p): void {}
            function give(RealPerson $x): void {
                take($x);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void MutuallyRecursiveShapes_DoNotInfiniteLoopOrFalselyReject()
    {
        // Regression: same root cause as NestedShapeMember_ClassChainAssigns, but with a
        // mutual (A -> B -> A) cycle instead of a one-way chain, exercising the coinductive
        // width-visit set alongside the alias-expansion fix.
        var errors = Check("""
            <?tyhp
            type A = object {
                public function b(): B;
            };
            type B = object {
                public function a(): A;
            };
            class RealA {
                public function b(): RealB { return new RealB(); }
            }
            class RealB {
                public function a(): RealA { return new RealA(); }
            }
            function take(A $a): void {}
            function give(RealA $x): void {
                take($x);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NominalAndShapeIntersection_ParsesAndChecksWithoutSpuriousNonClassError()
    {
        // Regression: `TypeDeclarationValidationRule.IsClassLike` did not recognize an object
        // shape as a legal intersection member, so every `Nominal & object { … }` alias RHS
        // (the story's own `TimestampedLogger` example) reported a spurious
        // `CheckerNonClassInIntersection` on the alias declaration itself.
        var errors = Check("""
            <?tyhp
            interface Logger {
                public function info(string $m): void;
            }
            type TimestampedLogger = Logger & object {
                public function getLastLogAt(): int;
            };
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NominalAndShapeIntersection_ObjectSource_Reports4358()
    {
        // Regression: `CheckerHelpers.IsObjectShapeAssignmentTarget` required *every*
        // intersection member to be a shape/`object`, so an intersection with a nominal parent
        // (`Logger & object { … }`) never got the specific TYHP4358 guard message for an
        // unnarrowed `object`/`mixed` source — only the generic incompatible-argument error.
        var errors = Check("""
            <?tyhp
            interface Logger {
                public function info(string $m): void;
            }
            type TimestampedLogger = Logger & object {
                public function getLastLogAt(): int;
            };
            function take(TimestampedLogger $l): void {}
            function fromObject(object $o): void {
                take($o);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeRequiresGuard);
    }

    [Fact]
    public void StaticShapeMethod_RequiresStaticSourceMethod()
    {
        var errors = Check("""
            <?tyhp
            type HasStatic = object {
                public static function make(): void;
            };
            class NonStaticMaker {
                public function make(): void {}
            }
            function take(HasStatic $x): void {}
            function give(NonStaticMaker $m): void {
                take($m);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ClassWithMatchingPublicConst_AssignsToShapeConst()
    {
        var errors = Check("""
            <?tyhp
            type HasFoo = object {
                public const int FOO = 1;
            };
            class WithFoo {
                public const int FOO = 1;
            }
            function take(HasFoo $x): void {}
            function give(WithFoo $w): void {
                take($w);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void MissingConst_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            type HasFoo = object {
                public const int FOO = 1;
            };
            class NoFoo {
                public function ping(): void {}
            }
            function take(HasFoo $x): void {}
            function give(NoFoo $n): void {
                take($n);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void MismatchedConstType_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            type HasFoo = object {
                public const int FOO = 1;
            };
            class StringFoo {
                public const string FOO = "x";
            }
            function take(HasFoo $x): void {}
            function give(StringFoo $s): void {
                take($s);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ProtectedConst_DoesNotSatisfyPublicShapeConst()
    {
        var errors = Check("""
            <?tyhp
            type HasFoo = object {
                public const int FOO = 1;
            };
            class HiddenFoo {
                protected const int FOO = 1;
            }
            function take(HasFoo $x): void {}
            function give(HiddenFoo $h): void {
                take($h);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    private static IReadOnlyList<IDiagnostic> Check(string tyhp, string? extraTyhpdef = null)
    {
        var result = extraTyhpdef is null
            ? IsolatedCompilation.ParseSnippet(tyhp)
            : IsolatedCompilation.ParseSnippet(tyhp, extraTyhpdef);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
