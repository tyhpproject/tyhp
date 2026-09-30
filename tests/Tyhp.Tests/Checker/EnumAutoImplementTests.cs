using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.11 B.1: every user <c>.tyhp</c> enum is <c>\UnitEnum</c> without a written
/// <c>implements</c>; a backed enum is also <c>\BackedEnum</c>. Assignability,
/// <c>instanceof</c>, and <c>cases</c> / <c>from</c> / <c>tryFrom</c> follow from that.
/// </summary>
[Trait("Category", "Checker")]
public class EnumAutoImplementTests
{
    [Fact]
    public void Check_UnitEnum_AssignableToUnitEnum()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit { case Hearts; }

            function take(\UnitEnum $e): void {}

            function f(Suit $s): void {
                take($s);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_UnitEnum_NotAssignableToBackedEnum()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit { case Hearts; }

            function take(\BackedEnum $e): void {}

            function f(Suit $s): void {
                take($s);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_UnitEnum_CasesTypesAsArrayOfSelf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit { case Hearts; }

            function takeCases(array<int, Suit> $cases): void {}

            function f(): void {
                takeCases(Suit::cases());
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_BackedEnum_AssignableToUnitEnumAndBackedEnum()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Size: int { case S = 1; }

            function takeUnit(\UnitEnum $e): void {}
            function takeBacked(\BackedEnum $e): void {}

            function f(Size $s): void {
                takeUnit($s);
                takeBacked($s);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_BackedEnum_FromReturnsStatic()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Size: int { case S = 1; }

            function takeSize(Size $s): void {}

            function f(): void {
                takeSize(Size::from(1));
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_BackedEnum_TryFromReturnsNullableStatic()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Size: int { case S = 1; }

            function takeSize(?Size $s): void {}

            function f(): void {
                takeSize(Size::tryFrom(1));
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_InstanceofUnitEnum_NarrowsMixed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit { case Hearts; }

            function take(\UnitEnum $e): void {}

            function f(mixed $x): void {
                if ($x instanceof \UnitEnum) {
                    take($x);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_InstanceofBackedEnum_NarrowsMixed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Size: int { case S = 1; }

            function take(\BackedEnum $e): void {}

            function f(mixed $x): void {
                if ($x instanceof \BackedEnum) {
                    take($x);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_Class_NotAssignableToUnitEnum()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function take(\UnitEnum $e): void {}

            function f(Foo $foo): void {
                take($foo);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    /// <summary>
    /// The enum-only member-lookup injection (<c>AppendEngineEnumAutoImplements</c>) must not leak
    /// <c>\BackedEnum::from</c> onto ordinary classes. <c>Foo</c> declares no <c>from</c> of its
    /// own, so a call to it must not be typed as returning <c>Foo</c> (which is what would happen
    /// if the injection ran for non-enum kinds and resolved <c>from</c> against <c>\BackedEnum</c>,
    /// overlaid to return <c>static</c>).
    /// </summary>
    [Fact]
    public void Check_NonEnumClass_DoesNotInheritBackedEnumFrom()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function takeFoo(Foo $f): void {}

            function f(): void {
                takeFoo(Foo::from(1));
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    /// <summary>
    /// Harvested <c>.tyhpdef</c> enum shells may keep a written <c>implements \UnitEnum,
    /// \BackedEnum</c> (Layer 1 dump honesty). The auto-implement injection must not double-add
    /// those engine interfaces on top of the explicit listing (no duplicate-interface explosion)
    /// and <c>CheckInterfaceImplementation</c> must stay quiet even though the shell does not
    /// itself provide a body for the engine-supplied members.
    /// </summary>
    [Fact]
    public void Check_HarvestedEnumWithExplicitImplements_NoDuplicateOrFalseErrors()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            enum HarvestSuit: int implements \BackedEnum, \UnitEnum {
                case Hearts = 1;
                case Spades = 2;
                static public function cases(): array;
                static public function from(int|string $value): \HarvestSuit;
                static public function tryFrom(int|string $value): ?\HarvestSuit;
            }
            """,
            """
            <?tyhp
            function takeUnit(\UnitEnum $e): void {}
            function takeBacked(\BackedEnum $e): void {}

            function f(\HarvestSuit $s): void {
                takeUnit($s);
                takeBacked($s);
                takeUnit(\HarvestSuit::from(1));
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedParameterType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
    }

    private static DiagnosticBag CompileAndCheckWithTyhpdef(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpPath = Path.Combine(tempDir, "app.tyhp");
        File.WriteAllText(tyhpPath, tyhp);
        var tyhpdefPath = Path.Combine(tempDir, "types.tyhpdef");
        File.WriteAllText(tyhpdefPath, tyhpdef);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.4",
                skipChecking: true,
                tyhpdefIncludePaths: [tyhpdefPath]);
            var result = compilationService.ParseFiles([tyhpPath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
