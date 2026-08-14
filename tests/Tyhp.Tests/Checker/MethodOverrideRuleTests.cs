using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers method-override validation (FOUND_BUGS item 12): final-method overrides and
/// signature-incompatible overrides must be diagnosed. Parent resolution must use the AST
/// <c>extends</c> fallback — <c>ExtendsType</c> alone is usually null.
/// </summary>
[Trait("Category", "Checker")]
public class MethodOverrideRuleTests
{
    [Fact]
    public void Check_OverrideOfFinalMethod_Reports4020()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public final function sealed(): void {}
            }
            class Child extends Base {
                public function sealed(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerFinalMethodOverridden);
    }

    [Fact]
    public void Check_OverrideOfFinalMethod_ThroughGrandparent_Reports4020()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public final function sealed(): void {}
            }
            class Middle extends Base {}
            class Child extends Middle {
                public function sealed(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerFinalMethodOverridden);
    }

    [Fact]
    public void Check_CompatibleOverride_DoesNotReport4020Or4118()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function greet(string $name): string {
                    return $name;
                }
            }
            class Child extends Base {
                public function greet(string $name): string {
                    return "hi " . $name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerFinalMethodOverridden
            || d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_IncompatibleReturnTypeOverride_Reports4118()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function returnsString(): string {
                    return "ok";
                }
            }
            class Child extends Base {
                public function returnsString(): int {
                    return 1;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_IncompatibleParameterTypeOverride_Reports4118()
    {
        // Parameter types must be contravariant: parent expects string, child narrowing to int is invalid.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function take(string $value): void {}
            }
            class Child extends Base {
                public function take(int $value): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_WidenedParameterTypeOverride_IsAccepted()
    {
        // Child accepting mixed is a valid contravariant widening of string.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function take(string $value): void {}
            }
            class Child extends Base {
                public function take(mixed $value): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_OverrideAddingAnOptionalParameter_IsAccepted()
    {
        // A call written against the base signature still binds, so this is a legal override.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function go(string $a): void {}
            }
            class Child extends Base {
                public function go(string $a, int $b = 0): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_OverrideAddingAVariadicParameter_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function go(string $a): void {}
            }
            class Child extends Base {
                public function go(string $a, int ...$rest): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_OverrideAddingARequiredParameter_Reports4118()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function go(string $a): void {}
            }
            class Child extends Base {
                public function go(string $a, int $b): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_OverrideDroppingAParameter_Reports4118()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function go(string $a, int $b): void {}
            }
            class Child extends Base {
                public function go(string $a): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_SameNameAsPrivateBaseMethod_IsNotAnOverride()
    {
        // PHP keeps private methods out of the inheritance slot, so the child neither overrides
        // `Base::hidden` nor has to stay signature-compatible with it.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private function hidden(string $a): void {}
            }
            class Child extends Base {
                private function hidden(int $a, int $b): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void Check_PrivateBaseMethodShadowingAFinalGrandparent_StillReports4020()
    {
        // Skipping the private declaration must not stop the walk: `Root::sealed` is still final.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Root {
                public final function sealed(): void {}
            }
            class Middle extends Root {
                private function sealed(): void {}
            }
            class Child extends Middle {
                public function sealed(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerFinalMethodOverridden);
    }

    [Fact]
    public void Check_ExtendingFinalClass_Reports4019()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Sealed {}
            class Child extends Sealed {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerFinalClassExtended);
    }

    [Fact]
    public void Check_UnimplementedInheritedAbstract_Reports4017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class Base {
                public abstract function work(): void;
            }
            class Child extends Base {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerAbstractMethodNotImplemented);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerAbstractMethodNotImplemented);
        error.Message.Should().Contain("Child");
        error.Message.Should().Contain("work");
        error.Message.Should().Contain("Base");
    }

    [Fact]
    public void Check_AbstractImplementedByMiddleClass_DoesNotReport4017OnLeaf()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class Base {
                public abstract function work(): void;
            }
            class Middle extends Base {
                public function work(): void {}
            }
            class Leaf extends Middle {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAbstractMethodNotImplemented);
    }

    [Fact]
    public void Check_AbstractImplementedByATrait_DoesNotReport4017()
    {
        // Trait members are not copied onto the using class's symbol, so the `use` clause has to be
        // consulted before concluding the method is missing.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Worker {
                public function work(): void {}
            }
            abstract class Base {
                public abstract function work(): void;
            }
            class Child extends Base {
                use Worker;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAbstractMethodNotImplemented);
    }

    [Fact]
    public void Check_AbstractImplementedByATraitOfATrait_DoesNotReport4017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Inner {
                public function work(): void {}
            }
            trait Outer {
                use Inner;
            }
            abstract class Base {
                public abstract function work(): void;
            }
            class Child extends Base {
                use Outer;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAbstractMethodNotImplemented);
    }

    [Fact]
    public void Check_TraitWithoutTheAbstractMethod_StillReports4017()
    {
        // Using an unrelated trait must not blanket-silence the check.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Helper {
                public function help(): void {}
            }
            abstract class Base {
                public abstract function work(): void;
            }
            class Child extends Base {
                use Helper;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerAbstractMethodNotImplemented);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_InheritedProperty_DoesNotReport()
    {
        // RestrictedFeatureRule must walk the fixed base chain, not ExtendsType alone.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $count = 0;
            }
            class Child extends Base {}

            function demo(): void {
                Child $c = new Child();
                $c->count = 5;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerDynamicPropertyProhibited);
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
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
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
