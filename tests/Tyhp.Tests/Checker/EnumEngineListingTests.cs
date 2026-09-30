using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.11 B.2: user <c>.tyhp</c> types must not list <c>\UnitEnum</c> /
/// <c>\BackedEnum</c>, and user enums must not redeclare <c>cases</c> / <c>from</c> /
/// <c>tryFrom</c>. Harvested <c>.tyhpdef</c> shells are a different AST and are
/// skipped structurally; <c>#[\Tyhp\Php]</c> is an ordinary version gate and does not
/// exempt a real <c>.tyhp</c> declaration.
/// </summary>
[Trait("Category", "Checker")]
public class EnumEngineListingTests
{
    [Fact]
    public void Check_UserClassImplementsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo implements \UnitEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        error.Message.Should().Contain("Foo");
        error.Message.Should().Contain("UnitEnum");
    }

    [Fact]
    public void Check_UserClassImplementsBackedEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo implements \BackedEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        error.Message.Should().Contain("Foo");
        error.Message.Should().Contain("BackedEnum");
    }

    [Fact]
    public void Check_InterfaceExtendsBackedEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I extends \BackedEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        error.Message.Should().Contain("I");
        error.Message.Should().Contain("BackedEnum");
    }

    [Fact]
    public void Check_InterfaceExtendsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I extends \UnitEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        error.Message.Should().Contain("I");
        error.Message.Should().Contain("UnitEnum");
    }

    [Fact]
    public void Check_TraitImplementsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T implements \UnitEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_TraitImplementsBackedEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T implements \BackedEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_TraitRequiresUnitEnumViaExtends_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T extends \UnitEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_EnumImplementsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E implements \UnitEnum { case A; }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        error.Message.Should().Contain("E");
        error.Message.Should().Contain("UnitEnum");
    }

    [Fact]
    public void Check_EnumImplementsBackedEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E: int implements \BackedEnum { case A = 1; }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_AnonymousClassImplementsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function make(): object {
                return new class implements \UnitEnum {};
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        error.Message.Should().Contain("anonymous class");
    }

    [Fact]
    public void Check_NamespacedInterfaceNamedUnitEnumExtendsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            interface UnitEnum extends \UnitEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_TyhpPhpGatedUserClassImplementsUnitEnum_Reports4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.1")]
            class Foo implements \UnitEnum {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_UserClassImplementsIterator_DoesNotReport4337()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class C implements \Iterator {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_BackedEnumRedeclaresFrom_Reports4338()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E: int {
                case S = 1;
                public static function from(int $v): self {
                    return self::S;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        error.Message.Should().Contain("E");
        error.Message.Should().Contain("from");
    }

    [Fact]
    public void Check_BackedEnumRedeclaresTryFrom_Reports4338()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E: int {
                case S = 1;
                public static function tryFrom(int $v): ?self {
                    return self::S;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        error.Message.Should().Contain("tryFrom");
    }

    [Fact]
    public void Check_UnitEnumRedeclaresCases_Reports4338()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E {
                case A;
                public static function cases(): array {
                    return [];
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        error.Message.Should().Contain("E");
        error.Message.Should().Contain("cases");
    }

    [Fact]
    public void Check_EnumUserMethod_DoesNotReport4338()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E {
                case A;
                public function label(): string {
                    return "A";
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
    }

    [Fact]
    public void Check_ClassMethodNamedFrom_DoesNotReport4338()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static function from(int $v): Foo {
                    return new Foo();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
    }

    [Fact]
    public void Check_HarvestedCoreEnumListingUnitEnum_DoesNotReport4337Or4338()
    {
        var diagnostics = CompileTyhpdefAsSource("""
            <?tyhpdef
            enum CoreStatus: int implements \UnitEnum, \BackedEnum {
                case Ok = 0;
                static public function cases(): array;
                static public function from(int|string $value): \CoreStatus;
                static public function tryFrom(int|string $value): ?\CoreStatus;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEngineEnumCannotBeListed);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerEnumEngineMethodRedeclared);
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

    private static DiagnosticBag CompileTyhpdefAsSource(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "lib.tyhpdef");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.4",
                skipChecking: true);
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
