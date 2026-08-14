using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers interface-method implementation checking (FOUND_BUGS item 27): the check must resolve
/// <c>implements</c> via the AST fallback — <c>ImplementsTypes</c> alone is usually empty. Also
/// covers trait-requirement walks that had the same blind spot.
/// </summary>
[Trait("Category", "Checker")]
public class InterfaceImplementationRuleTests
{
    [Fact]
    public void Check_UnimplementedInterfaceMethod_Reports4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            class C implements I {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
        error.Message.Should().Contain("C");
        error.Message.Should().Contain("work");
        error.Message.Should().Contain("I");
    }

    [Fact]
    public void Check_ImplementedInterfaceMethod_DoesNotReport4018()
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
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_InterfaceMethodFromParentInterface_Reports4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Base {
                public function work(): void;
            }
            interface Child extends Base {}
            class C implements Child {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_InterfaceInheritedThroughBaseClass_Reports4018OnConcreteLeaf()
    {
        // Abstract parents may leave interface methods open; the concrete leaf must still provide them.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            abstract class Base implements I {}
            class Leaf extends Base {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented
            && d.Message.Contains("Leaf"));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented
            && d.Message.Contains("Base"));
    }

    [Fact]
    public void Check_AbstractClassMayOmitInterfaceMethods()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            abstract class Base implements I {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_InterfaceMethodSuppliedByTrait_DoesNotReport4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            trait Worker {
                public function work(): void {}
            }
            class C implements I {
                use Worker;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_InterfaceMethodSuppliedByParent_DoesNotReport4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            class Base implements I {
                public function work(): void {}
            }
            class Leaf extends Base {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_InterfaceDefaultMethod_DoesNotReport4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void {
                    // default body
                }
            }
            class C implements I {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_DefaultBodyInExtendingInterface_SatisfiesBaseDeclaration()
    {
        // `Child` supplies the body, so the class inherits it — `Base`'s bodiless declaration of the
        // same name must not be reported.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Base {
                public function work(): void;
            }
            interface Child extends Base {
                public function work(): void {}
            }
            class C implements Child {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_DefaultBodyInOneOfTwoInterfaces_SatisfiesBoth()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface A {
                public function work(): void;
            }
            interface B {
                public function work(): void {}
            }
            class C implements A, B {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_PrivateInterfaceMethod_DoesNotReport4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                private function helper(): void;
                public function work(): void;
            }
            class C implements I {
                public function work(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_InterfaceMethodSuppliedByTraitAlias_DoesNotReport4018()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                public function work(): void;
            }
            trait Worker {
                public function doWork(): void {}
            }
            class C implements I {
                use Worker { doWork as work; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_ParentOperatorOverloadSatisfiesInheritedInterface()
    {
        // `Money`'s `operator convert(self): string` generates `__toString`. It never becomes a member
        // symbol, so a child inheriting the interface has to see it on the parent's AST.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Strish {
                public function __toString(): string;
            }
            class Money implements Strish {
                operator convert(self $value): string { return 'x'; }
            }
            class Derived extends Money {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_TraitRequiresExtends_Reports4044WhenMissing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class ReqEntity {}
            trait Timestamped extends ReqEntity {}
            class Post {
                use Timestamped;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraitRequirementNotMet);
    }

    [Fact]
    public void Check_TraitRequiresExtends_DoesNotReport4044WhenSatisfied()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class ReqEntity {}
            trait Timestamped extends ReqEntity {}
            class Post extends ReqEntity {
                use Timestamped;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraitRequirementNotMet);
    }

    [Fact]
    public void Check_TraitRequiresImplements_Reports4045WhenMissing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface CacheableContract {
                public function cacheKey(): string;
            }
            trait Cacheable implements CacheableContract {}
            class Post {
                use Cacheable;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraitRequirementImplNotMet);
    }

    [Fact]
    public void Check_TraitRequiresImplements_DoesNotReport4045WhenSatisfied()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface CacheableContract {
                public function cacheKey(): string;
            }
            trait Cacheable implements CacheableContract {}
            class Post implements CacheableContract {
                use Cacheable;
                public function cacheKey(): string { return ""; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraitRequirementImplNotMet
            || d.Code == MessageCode.CheckerInterfaceMethodNotImplemented);
    }

    [Fact]
    public void Check_NestedTraitRequirement_IsEnforcedOnUsingClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class ReqEntity {}
            trait Inner extends ReqEntity {}
            trait Outer {
                use Inner;
            }
            class Post {
                use Outer;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraitRequirementNotMet);
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
