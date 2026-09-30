using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Bare <c>new Generic()</c> instantiates omitted type arguments from the expected
/// type of this expression (return, typed local, assignment, argument).
/// </summary>
[Trait("Category", "Checker")]
public class GenericNewContextInferenceTests
{
    [Fact]
    public void Check_BareGenericStructNew_InfersFromReturnType()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): Box<string> {
                return new Box() with [value => "x"];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_RejectsIncompatibleWithValue()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): Box<string> {
                return new Box() with [value => 42];
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerTypeMismatch, Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_InfersFromTypedLocal()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): void {
                Box<int> $box = new Box() with [value => 1];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_InfersFromAssignmentTarget()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): void {
                Box<string> $box;
                $box = new Box() with [value => "x"];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_InfersFromArgument()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function take(Box<int> $box): void {}
            function make(): void {
                take(new Box() with [value => 7]);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructExtends_InfersFromReturnType()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Base<T> = struct {
                T $value;
            };
            type Child<T> = struct extends Base<T> {
                int $count;
            };
            function make(): Child<string> {
                return new Child() with [value => "x", count => 1];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_DoesNotStealEnclosingReturnType()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): Box<string> {
                Box<int> $other = new Box() with [value => 1];
                return new Box() with [value => "x"];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericClassNew_InfersConstructorFromReturnType()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            class Box<T> {
                public function __construct(T $value): void {}
            }
            function make(): Box<string> {
                return new Box("x");
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericClassNew_RejectsIncompatibleConstructorArg()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            class Box<T> {
                public function __construct(T $value): void {}
            }
            function make(): Box<string> {
                return new Box(42);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType, Describe(errors));
    }

    [Fact]
    public void Check_BareGenericClassNew_ExplicitTypeArgumentsStillWin()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            class Box<T> {
                public function __construct(T $value): void {}
            }
            function make(): Box<string> {
                return new Box<int>(1);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType
                || e.Code == MessageCode.CheckerTypeMismatch, Describe(errors));
    }

    [Fact]
    public void Check_NonGenericNew_Unchanged()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            class Holder {}
            function make(): Holder {
                return new Holder();
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_NullableExpectedGeneric_InfersInnerArguments()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): ?Box<string> {
                return new Box() with [value => "x"];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_ParentExpectedTypeDoesNotInferSubclassNew()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Base<T> = struct {
                T $value;
            };
            type Child<T> = struct extends Base<T> {
                int $extra;
            };
            function make(): Base<string> {
                return new Child() with [value => "x", extra => 1];
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerTypeMismatch, Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_CallArgumentContextWinsOverOuterReturnLeak()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function unwrap(Box<int> $inner): int {
                return $inner->value;
            }
            function make(): Box<string> {
                return new Box() with [value => \strval(unwrap(new Box() with [value => 5]))];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_UnionExpectedType_DisagreeingMembersFallBackToDefaults()
    {
        // Box<T> has no default for T, so with no unique expected instantiation both branches
        // fall back to the pre-#64 unbound-generic-parameter behavior (same treatment on both
        // sides — neither branch is silently instantiated as the *other* branch's member type).
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(bool $c): Box<int>|Box<string> {
                if ($c) {
                    return new Box() with [value => 1];
                }

                return new Box() with [value => "x"];
            }
            """);

        errors.Should().HaveCount(2, Describe(errors));
        errors.Should().OnlyContain(
            e => e.Code == MessageCode.CheckerTypeMismatch && IsUnboundGenericParameterMismatch(e),
            Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_UnionExpectedType_AgreeingMembersInfer()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(bool $c): ?Box<string> {
                if ($c) {
                    return new Box() with [value => "a"];
                }

                return null;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Check_BareGenericStructNew_ArrayLiteralElementsDoNotInferFromOuterArrayReturn()
    {
        // The outer expected type at the array-literal `new` sites is `array<Box<string>>`,
        // whose nominal symbol is `array`, not `Box` — so `TryGetTypeArguments` never matches
        // and both elements fall back to the same unbound-`T` treatment as
        // `Check_BareGenericStructNew_UnionExpectedType_DisagreeingMembersFallBackToDefaults`.
        // If the outer `array<Box<string>>` had incorrectly leaked through, only the
        // int-valued element (`value => 1`) would fail while the string-valued element
        // succeeded; both failing the same way confirms no such leak.
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            type Box<T> = struct {
                T $value;
            };
            function make(): array<Box<string>> {
                return [new Box() with [value => 1], new Box() with [value => "ok"]];
            }
            """);

        errors.Should().HaveCount(2, Describe(errors));
        errors.Should().OnlyContain(
            e => e.Code == MessageCode.CheckerTypeMismatch && IsUnboundGenericParameterMismatch(e),
            Describe(errors));
    }

    [Fact]
    public void Check_BareGenericClassNew_ParentExpectedTypeDoesNotInferSubclassNew()
    {
        var errors = UserErrors("""
            <?tyhp
            namespace Test;
            class Base<T> {
                public function __construct(T $value): void {}
            }
            class Child<T> extends Base<T> {
                public function __construct(T $value): void {
                    parent::__construct($value);
                }
            }
            function make(): Base<string> {
                return new Child("x");
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType
                || e.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    private static bool IsUnboundGenericParameterMismatch(Tyhp.Domain.Diagnostics.IDiagnostic diagnostic) =>
        diagnostic.FormatParams.Length == 2 && Equals(diagnostic.FormatParams[1], "T");

    private static IReadOnlyList<Tyhp.Domain.Diagnostics.IDiagnostic> UserErrors(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static string Describe(IReadOnlyList<Tyhp.Domain.Diagnostics.IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
