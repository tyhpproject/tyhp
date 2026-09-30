using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story271")]
public class CallableShapeAssignabilityTests
{
    private readonly SymbolTree _symbolTree;
    private readonly GlobalScope _globalScope;

    public CallableShapeAssignabilityTests()
    {
        _symbolTree = new SymbolTree(new SymbolIdentifier([]));
        _globalScope = _symbolTree.GlobalScope;
        Tyhp.TyhpLang.Binder.BuiltIn.Types.PopulateGlobal(_globalScope);
    }

    [Fact]
    public void MatchingFunctionFnFccAndInvoke_AssignToShape()
    {
        var errors = Check("""
            <?tyhp
            class Handler {
                public function __invoke(int $i): string {
                    return "h";
                }
            }
            function mapper(int $i): string {
                return "m";
            }
            function take(callable(int $i): string $cb): void {}
            function main(Handler $h): void {
                take(mapper(...));
                take(fn(int $i): string => "f");
                take(function (int $i): string { return "g"; });
                take($h);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void BareCallable_DoesNotAssignToShape_Reports4360()
    {
        var errors = Check("""
            <?tyhp
            function take(callable(int $i): string $cb): void {}
            function fromBare(callable $c): void {
                take($c);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerCallableShapeRequiresGuard,
            Describe(errors));
    }

    [Fact]
    public void Mixed_DoesNotAssignToShape_Reports4360()
    {
        var errors = Check("""
            <?tyhp
            function take(callable(int $i): string $cb): void {}
            function fromMixed(mixed $m): void {
                take($m);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerCallableShapeRequiresGuard,
            Describe(errors));
    }

    [Fact]
    public void Shape_AssignsToBareCallable()
    {
        var errors = Check("""
            <?tyhp
            function take(callable $c): void {}
            function give(callable(int $i): string $cb): void {
                take($cb);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ValuelessEquals_IsSameTypeAsInitializerEquals()
    {
        var errors = Check("""
            <?tyhp
            function takeValueless(callable(int $i, bool $b =): string $cb): void {}
            function takeInit(callable(int $i, bool $b = false): string $cb): void {}
            function give(callable(int $i, bool $b = false): string $a): void {
                takeValueless($a);
                takeInit($a);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void InitializerValue_IsIgnoredForAssignability()
    {
        var errors = Check("""
            <?tyhp
            function takeFalse(callable(int, bool = false): string $cb): void {}
            function takeTrue(callable(int, bool = true): string $cb): void {}
            function give(callable(int, bool = false): string $a): void {
                takeTrue($a);
            }
            function giveBack(callable(int, bool = true): string $a): void {
                takeFalse($a);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NamedAndUnnamedShapes_AreInterchangeablePositionally()
    {
        var errors = Check("""
            <?tyhp
            function takeNamed(callable(int $i, bool $b =): string $cb): void {
                $cb(1);
                $cb(1, true);
            }
            function takeUnnamed(callable(int, bool=): string $cb): void {
                $cb(1);
                $cb(1, true);
            }
            function giveNamed(callable(int $i, bool $b =): string $cb): void {
                takeUnnamed($cb);
            }
            function giveUnnamed(callable(int, bool=): string $cb): void {
                takeNamed($cb);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void FunctionWithOptionalDefault_SatisfiesShapeWithDifferentInitializer()
    {
        var errors = Check("""
            <?tyhp
            function f(int $i, bool $b = true): string {
                return "x";
            }
            function take(callable(int, bool = false): string $cb): void {}
            function main(): void {
                take(f(...));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtraOptionalParametersOnImplementation_AssignToShorterShape()
    {
        var errors = Check("""
            <?tyhp
            function f(int $i, bool $b = true, string $extra = "x"): string {
                return "x";
            }
            function take(callable(int $i): string $cb): void {}
            function main(): void {
                take(f(...));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ExtraRequiredParameterOnImplementation_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            function f(int $i, bool $b): string {
                return "x";
            }
            function take(callable(int $i): string $cb): void {}
            function main(): void {
                take(f(...));
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void ParameterContravariance_WiderImplParam_Assigns()
    {
        var errors = Check("""
            <?tyhp
            function impl(int|string $x): void {}
            function take(callable(int $i): void $cb): void {}
            function main(): void {
                take(impl(...));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void ParameterContravariance_NarrowerImplParam_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            function impl(int $x): void {}
            function take(callable(int|string $i): void $cb): void {}
            function main(): void {
                take(impl(...));
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void ReturnCovariance_NarrowerImplReturn_Assigns()
    {
        var errors = Check("""
            <?tyhp
            function impl(): int {
                return 1;
            }
            function take(callable(): int|string $cb): void {}
            function main(): void {
                take(impl(...));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NamedArgument_ChecksAgainstShapeName()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(string $name): void $cb): void {
                $cb(name: "x");
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NamedArgument_WrongType_ReportsIncompatible()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(string $name): void $cb): void {
                $cb(name: 1);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(errors));
    }

    [Fact]
    public void NamedArgument_UnknownNameOnNamedShape_ReportsUnknown()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(string $name): void $cb): void {
                $cb(wrong: "x");
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerUnknownNamedArgument,
            Describe(errors));
    }

    [Fact]
    public void NamedOptionalArgument_CheckedOnNamedShape()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(int $i, bool $b =): string $cb): void {
                $cb(i: 1, b: true);
                $cb(b: true, i: 1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NamedOptionalArgument_WrongTypeOnNamedShape_ReportsIncompatible()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(int $i, bool $b =): string $cb): void {
                $cb(i: 1, b: "nope");
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(errors));
    }

    [Fact]
    public void UnnamedShape_NamedArgumentsAreNotCheckedAgainstInventedNames()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(int, bool=): string $cb): void {
                $cb(b: true);
                $cb(1, b: "not-a-bool");
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnknownNamedArgument,
            Describe(errors));
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(errors));
    }

    [Fact]
    public void NamedShape_NamedArgumentFillsRequiredSlot()
    {
        var errors = Check("""
            <?tyhp
            function demo(callable(int $i, bool $b =): string $cb): void {
                $cb(i: 1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void InvokeObject_MismatchedSignature_DoesNotAssign()
    {
        var errors = Check("""
            <?tyhp
            class Handler {
                public function __invoke(): int {
                    return 1;
                }
            }
            function take(callable(int $i): string $cb): void {}
            function demo(Handler $h): void {
                take($h);
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            Describe(errors));
    }

    [Fact]
    public void AreTypesEqual_NamedVsUnnamedOptionalShapes()
    {
        var named = CallableArityFacetBuilder.Build(
            [CheckedTypes.Int, CheckedTypes.Bool],
            [(false, false), (true, false)],
            CheckedTypes.String,
            ["i", "b"]);
        var unnamed = CallableArityFacetBuilder.Build(
            [CheckedTypes.Int, CheckedTypes.Bool],
            [(false, false), (true, false)],
            CheckedTypes.String);

        TypeComparer.AreTypesEqual(named, unnamed).Should().BeTrue();
        TypeComparer.IsAssignableTo(named, unnamed, _symbolTree, _globalScope).Should().BeTrue();
        TypeComparer.IsAssignableTo(unnamed, named, _symbolTree, _globalScope).Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_UntypedCallable_DoesNotAssignToShape()
    {
        var untyped = CheckedTypes.FromSymbol(TypeComparer.ResolveBuiltIn("callable", _globalScope)!);
        var shape = new CallableCheckedType([CheckedTypes.Int], CheckedTypes.String, ["i"]);

        TypeComparer.IsAssignableTo(untyped, shape, _symbolTree, _globalScope).Should().BeFalse();
        TypeComparer.IsAssignableTo(shape, untyped, _symbolTree, _globalScope).Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_ParameterContravarianceAndReturnCovariance()
    {
        var impl = new CallableCheckedType(
            [new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String])],
            CheckedTypes.Int);
        var target = new CallableCheckedType(
            [CheckedTypes.Int],
            new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]));

        TypeComparer.IsAssignableTo(impl, target, _symbolTree, _globalScope).Should().BeTrue();
        TypeComparer.IsAssignableTo(target, impl, _symbolTree, _globalScope).Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_ExtraOptionalFacets_AssignToShorterShape()
    {
        var longer = CallableArityFacetBuilder.Build(
            [CheckedTypes.Int, CheckedTypes.Bool],
            [(false, false), (true, false)],
            CheckedTypes.String,
            ["i", "b"]);
        var shorter = new CallableCheckedType([CheckedTypes.Int], CheckedTypes.String, ["i"]);

        TypeComparer.IsAssignableTo(longer, shorter, _symbolTree, _globalScope).Should().BeTrue();
        TypeComparer.IsAssignableTo(shorter, longer, _symbolTree, _globalScope).Should().BeFalse();
    }

    private static IReadOnlyList<IDiagnostic> Check(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
