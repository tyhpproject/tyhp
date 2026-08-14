using System.Diagnostics;
using System.Text;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers base-target validation (FOUND_BUGS items 29 and 32): unresolved
/// <c>extends</c>/<c>implements</c>, circular inheritance, and wrong-kind targets must be diagnosed.
/// Raw <c>IClassName</c> clauses leave <c>ExtendsType</c>/<c>ImplementsTypes</c> empty, so the
/// binder's 3017/3018 path never fires — the checker owns the report. Trait
/// <c>extends</c>/<c>implements</c> are requirements (not inheritance), so wrong-kind rules skip them.
/// </summary>
[Trait("Category", "Checker")]
public class InheritanceTargetRuleTests
{
    [Fact]
    public void Check_MissingBaseClass_Reports3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Child extends TotallyMissingBase {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
        error.Message.Should().Contain("TotallyMissingBase");
    }

    [Fact]
    public void Check_MissingImplementsInterface_Reports3018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C implements TotallyMissingInterface {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.BinderUnresolvedImplementsType);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.BinderUnresolvedImplementsType);
        error.Message.Should().Contain("TotallyMissingInterface");
    }

    [Fact]
    public void Check_MissingInterfaceExtends_Reports3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Child extends TotallyMissingBase {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
        error.Message.Should().Contain("TotallyMissingBase");
    }

    [Fact]
    public void Check_SelfExtends_Reports3006()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends A {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularInheritance);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderCircularInheritance);
        error.Message.Should().Contain("A");
    }

    [Fact]
    public void Check_TwoClassCycle_Reports3006()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends B {}
            class B extends A {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularInheritance);
        diagnostics.Errors.Count(d => d.Code == MessageCode.BinderCircularInheritance)
            .Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Check_ThreeClassCycle_Reports3006()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends B {}
            class B extends C {}
            class C extends A {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularInheritance);
    }

    [Fact]
    public void Check_TwoInterfaceCycle_Reports3006()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface A extends B {}
            interface B extends A {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularInheritance);
    }

    [Fact]
    public void Check_InterfaceDiamond_DoesNotReport3006()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Base {}
            interface Left extends Base {}
            interface Right extends Base {}
            interface Child extends Left, Right {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderCircularInheritance
            || d.Code == MessageCode.BinderUnresolvedExtendsType
            || d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Check_InterfaceCycleBehindADiamond_Reports3006()
    {
        // `Shared` is fully explored (and recorded acyclic) down the `Left` branch before the walk
        // reaches the cycle down the `Right` branch. Recording it must not hide the back edge.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Shared {}
            interface Left extends Shared {}
            interface Right extends Shared, Loop {}
            interface Loop extends Child {}
            interface Child extends Left, Right {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularInheritance);
    }

    [Fact]
    public void Check_WideInterfaceDiamond_StaysCleanAndTerminates()
    {
        // Every level extends both interfaces of the level below, so the DAG has 2^levels distinct
        // root-to-leaf paths. Re-walking each one takes minutes; recording proven-acyclic interfaces
        // keeps it linear.
        var source = new StringBuilder("<?tyhp\ninterface I0a {}\ninterface I0b {}\n");
        for (var level = 1; level <= 26; level++)
        {
            source.AppendLine($"interface I{level}a extends I{level - 1}a, I{level - 1}b {{}}");
            source.AppendLine($"interface I{level}b extends I{level - 1}a, I{level - 1}b {{}}");
        }

        var stopwatch = Stopwatch.StartNew();
        var diagnostics = CompileAndCheck(source.ToString());
        stopwatch.Stop();

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderCircularInheritance
            || d.Code == MessageCode.BinderUnresolvedExtendsType);
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromMinutes(1),
            "the interface cycle walk must not re-explore every path through a diamond");
    }

    [Fact]
    public void Check_MissingTraitExtendsRequirement_Reports3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T extends TotallyMissingBase {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Check_MissingTraitImplementsRequirement_Reports3018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T implements TotallyMissingInterface {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Check_MissingEnumImplements_Reports3018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E: string implements TotallyMissingInterface { case A = 'a'; }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Check_AnonymousClassMissingBase_Reports3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            $x = new class extends TotallyMissingBase {};
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    // FOUND_BUGS #33: resolution must reach anonymous classes nested in method bodies.
    [Fact]
    public void Check_AnonymousClassInsideMethodMissingBase_Reports3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go(): void {
                    $x = new class extends TotallyMissingBase {};
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Check_AnonymousClassInsideMethodValidImplements_DoesNotReport301()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Foo {}
            class C {
                public function go(): void {
                    $x = new class implements Foo {
                        public function noop(): void {}
                    };
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedExtendsType
            || d.Code == MessageCode.BinderUnresolvedImplementsType
            || d.Code == MessageCode.BinderInvalidImplementsTypeKind);
    }

    [Fact]
    public void Check_GenericBase_DoesNotReport3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box<T> {}
            class IntBox extends Box<int> {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedExtendsType
            || d.Code == MessageCode.BinderCircularInheritance);
    }

    [Fact]
    public void Check_BuiltInBaseAndInterface_DoNotReportUnresolved()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyError extends \Exception implements \Stringable {
                public function __toString(): string { return ''; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedExtendsType
            || d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Check_ValidSameNamespaceBareBase_DoesNotReport3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App {
                class Base {}
                class Child extends Base {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedExtendsType
            || d.Code == MessageCode.BinderCircularInheritance);
    }

    [Fact]
    public void Check_ValidImportedBase_DoesNotReport3017()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Other {
                class Base {}
            }
            namespace App {
                use Other\Base;
                class Child extends Base {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedExtendsType
            || d.Code == MessageCode.BinderCircularInheritance);
    }

    [Fact]
    public void Check_ValidImplements_DoesNotReport3018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            class C implements I {
                public function work(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedImplementsType
            || d.Code == MessageCode.BinderCircularInheritance
            || d.Code == MessageCode.BinderInvalidImplementsTypeKind);
    }

    [Fact]
    public void Check_ClassExtendsInterface_Reports3023()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {}
            class C extends I {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        error.Message.Should().Contain("I");
        error.Message.Should().Contain("interface");
        error.Message.Should().Contain("class");
    }

    [Fact]
    public void Check_ClassImplementsClass_Reports3024()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A {}
            class C implements A {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidImplementsTypeKind);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderInvalidImplementsTypeKind);
        error.Message.Should().Contain("A");
        error.Message.Should().Contain("class");
        error.Message.Should().Contain("interface");
    }

    [Fact]
    public void Check_InterfaceExtendsClass_Reports3023()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A {}
            interface I extends A {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        error.Message.Should().Contain("A");
        error.Message.Should().Contain("class");
        error.Message.Should().Contain("interface");
    }

    [Fact]
    public void Check_ClassExtendsTrait_Reports3023()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {}
            class C extends T {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        error.Message.Should().Contain("T");
        error.Message.Should().Contain("trait");
    }

    [Fact]
    public void Check_ClassExtendsEnum_Reports3023()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E { case A; }
            class C extends E {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        var error = diagnostics.Errors.First(d => d.Code == MessageCode.BinderInvalidExtendsTypeKind);
        error.Message.Should().Contain("E");
        error.Message.Should().Contain("enum");
    }

    [Fact]
    public void Check_ClassImplementsTrait_Reports3024()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {}
            class C implements T {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidImplementsTypeKind);
    }

    [Fact]
    public void Check_EnumImplementsClass_Reports3024()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A {}
            enum E: string implements A { case X = 'x'; }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderInvalidImplementsTypeKind);
    }

    [Fact]
    public void Check_TraitExtendsClass_DoesNotReportWrongKind()
    {
        // Trait extends/implements are requirements, not inheritance — kind rules must not fire.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Entity {}
            trait T extends Entity {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderInvalidExtendsTypeKind
            || d.Code == MessageCode.BinderInvalidImplementsTypeKind
            || d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Check_TraitImplementsInterface_DoesNotReportWrongKind()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface SerializableMarker {}
            trait T implements SerializableMarker {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderInvalidExtendsTypeKind
            || d.Code == MessageCode.BinderInvalidImplementsTypeKind
            || d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Check_TraitExtendsInterface_DoesNotReportWrongKind()
    {
        // Deliberately odd requirement target — still not real inheritance, so 3023 stays quiet.
        // Satisfaction is CheckTraitRequirements' job when the trait is used.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {}
            trait T extends I {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderInvalidExtendsTypeKind
            || d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Check_ValidClassExtendsClass_DoesNotReport3023()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {}
            class Child extends Base {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderInvalidExtendsTypeKind
            || d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Check_ValidInterfaceExtendsInterface_DoesNotReport3023()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Base {}
            interface Child extends Base {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderInvalidExtendsTypeKind
            || d.Code == MessageCode.BinderUnresolvedExtendsType);
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
