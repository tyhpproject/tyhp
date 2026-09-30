using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers attribute validation on declarations (FOUND_BUGS item 28): class members bypass
/// <c>CheckNode</c>, so <see cref="Tyhp.TyhpLang.Checker.Rules.AttributeRule"/> must be invoked
/// explicitly from the <c>CheckObjectBody</c> member paths (methods, properties, enum cases and
/// class constants), and each mistake must produce exactly one diagnostic.
/// </summary>
[Trait("Category", "Checker")]
public class AttributeRuleTests
{
    [Fact]
    public void Check_NonAttributeClassOnMethod_Reports4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {}
            class C {
                #[Plain]
                public function go(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    [Fact]
    public void Check_NonAttributeClassOnProperty_Reports4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {}
            class C {
                #[Plain]
                public int $x = 0;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    [Fact]
    public void Check_NonAttributeClassOnFunction_Reports4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {}
            #[Plain]
            function go(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    [Fact]
    public void Check_NonAttributeClassOnEnumCase_Reports4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {}
            enum E {
                #[Plain]
                case A;
            }
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    [Fact]
    public void Check_NonAttributeClassOnClassConstant_Reports4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {}
            class C {
                #[Plain]
                public const int FOO = 1;
            }
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    [Fact]
    public void Check_OverrideOnNonOverridingMethod_Reports4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                #[\Override]
                public function alone(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerOverrideNotOverriding).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_OverrideOnOverridingMethod_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function greet(): void {}
            }
            class Child extends Base {
                #[\Override]
                public function greet(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_OverrideOnOverridingMethod_ThroughGrandparent_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function greet(): void {}
            }
            class Middle extends Base {}
            class Child extends Middle {
                #[\Override]
                public function greet(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_OverrideOnImplementedInterfaceMethod_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Greeter { public function greet(): void; }
            class C implements Greeter {
                #[\Override]
                public function greet(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_OverrideOnExtendedInterfaceMethod_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Base { public function greet(): void; }
            interface Middle extends Base {}
            class C implements Middle {
                #[\Override]
                public function greet(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_OverrideInsideTrait_DoesNotReport4129()
    {
        // PHP resolves a trait's #[Override] against the composing class, not the trait.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {
                #[\Override]
                public function greet(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_OverrideOnClass_ReportsTargetMismatchOnly()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Override]
            class C {}
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeTargetMismatch).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_OverrideOnProperty_ReportsTargetMismatchOnly()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                #[\Override]
                public int $x = 0;
            }
            """);

        var mismatches = diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerAttributeTargetMismatch)
            .ToList();

        mismatches.Should().ContainSingle();
        // The target is named in the author's vocabulary, not as an AST class name.
        mismatches[0].Message.Should().Contain("property").And.NotContain("Ast");
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_NoParent_Reports4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                #[\Override]
                public int $x = 0;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerOverrideNotOverriding).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass
            || d.Code == MessageCode.CheckerOverrideOnConstructor);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_ParentProperty_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                #[\Override]
                public int $x = 1;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_GrandparentProperty_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $x = 0;
            }
            class Middle extends Base {}
            class Child extends Middle {
                #[\Override]
                public int $x = 1;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_InterfaceProperty_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface HasX { public int $x; }
            class C implements HasX {
                #[\Override]
                public int $x = 0;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_ExtendedInterfaceProperty_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Base { public int $x; }
            interface Middle extends Base {}
            class C implements Middle {
                #[\Override]
                public int $x = 0;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_PrivateParent_Reports4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private int $x = 0;
            }
            class Child extends Base {
                #[\Override]
                public int $x = 1;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    [Fact]
    public void Check_OverrideOnProperty_Php85_InsideTrait_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {
                #[\Override]
                public int $x = 0;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    [Fact]
    public void Check_OverrideOnPromotedProperty_Php85_ParentProperty_DoesNotReport4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                public function __construct(
                    #[\Override]
                    public int $x
                ) {}
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerOverrideOnConstructor);
    }

    [Fact]
    public void Check_BoundOverrideOnPromotedProperty_Php85_ParentProperty_DoesNotReport4127Or4129()
    {
        // Isolated compiles leave \Override unbound, which skips the generic TARGET_* check.
        // A real bound stub with TARGET_METHOD|TARGET_PROPERTY (Core's #[Attribute(12)]) must
        // still treat a promoted constructor parameter as a property target, not a parameter.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD | \Attribute::TARGET_PROPERTY)]
            final class Override {}

            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                public function __construct(
                    #[\Override]
                    public int $x
                ) {}
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerOverrideOnConstructor
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_BoundOverrideOnPromotedProperty_Php85_NoParent_Reports4129Not4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD | \Attribute::TARGET_PROPERTY)]
            final class Override {}

            class C {
                public function __construct(
                    #[\Override]
                    public int $x
                ) {}
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerOverrideNotOverriding).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerOverrideOnConstructor
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_BoundOverrideOnPromotedProperty_Below85_Reports4127Not4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD | \Attribute::TARGET_PROPERTY)]
            final class Override {}

            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                public function __construct(
                    #[\Override]
                    public int $x
                ) {}
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeTargetMismatch).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerOverrideOnConstructor
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_PropertyOnlyAttributeOnPromotedConstructorParameter_DoesNotReport4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_PROPERTY)]
            final class PropOnly {}

            class C {
                public function __construct(#[PropOnly] public int $x) {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_ParameterOnlyAttributeOnPromotedConstructorParameter_DoesNotReport4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_PARAMETER)]
            final class ParamOnly {}

            class C {
                public function __construct(#[ParamOnly] public int $x) {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Check_OverrideOnConstruct_Reports4339_EvenWhenParentConstructorExists(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function __construct() {}
            }
            class Child extends Base {
                #[\Override]
                public function __construct() {}
            }
            """, phpVersion);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideOnConstructor);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerOverrideOnConstructor).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_OverrideOnConstruct_WithoutParent_Reports4339Not4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                #[\Override]
                public function __construct() {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideOnConstructor);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_OverrideOnConstruct_InsideTrait_Reports4339()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {
                #[\Override]
                public function __construct() {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideOnConstructor);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_DelayedTargetValidation_OverrideOnProperty_Php85_ParentProperty_NoTargetMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                #[\DelayedTargetValidation]
                #[\Override]
                public int $x = 1;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_DelayedTargetValidation_OverrideOnProperty_Php85_NoParent_Still4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                #[\DelayedTargetValidation]
                #[\Override]
                public int $x = 0;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerOverrideNotOverriding).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Check_DelayedTargetValidation_OverrideOnProperty_Below85_StillTargetMismatch(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                #[\DelayedTargetValidation]
                #[\Override]
                public int $x = 1;
            }
            """, phpVersion);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeTargetMismatch).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
    }

    [Fact]
    public void Check_DelayedTargetValidation_OverrideOnNonOverridingMethod_Php85_Still4129()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                #[\DelayedTargetValidation]
                #[\Override]
                public function alone(): void {}
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOverrideNotOverriding);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerOverrideNotOverriding).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    [Fact]
    public void Check_DelayedTargetValidation_OverrideOnClass_Php85_SkipsTargetMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\DelayedTargetValidation]
            #[\Override]
            class C {}
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_DelayedTargetValidation_UserlandTargetMismatch_Php85_Still4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD)]
            class OnlyMethod {}

            class C {
                #[\DelayedTargetValidation]
                #[OnlyMethod]
                public int $x = 0;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeTargetMismatch).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_DelayedTargetValidation_ReversedAttributeOrder_Php85_StillSkips()
    {
        // Locked decision does not require a specific attribute order; #[\Override] written
        // before #[\DelayedTargetValidation] must skip the property TARGET mismatch exactly
        // like the DelayedTargetValidation-first order covered above.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $x = 0;
            }
            class Child extends Base {
                #[\Override]
                #[\DelayedTargetValidation]
                public int $x = 1;
            }
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerOverrideNotOverriding
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_DelayedTargetValidation_UserShadowClass_Php85_DoesNotSkipTargetMismatch()
    {
        // A userland class literally named `DelayedTargetValidation` (bound to a real, non-Core
        // source file) must not be mistaken for the PHP Core attribute just because the name
        // matches — the skip is keyed off the *source* of the bound symbol, not the bare name.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class DelayedTargetValidation {}

            #[DelayedTargetValidation]
            #[\Override]
            function standalone(): void {}
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeTargetMismatch).Should().Be(1);
    }

    [Fact]
    public void Check_DelayedTargetValidation_NamespacedUserShadowClass_Php85_DoesNotSkipTargetMismatch()
    {
        // Same shadow scenario as above, but the userland lookalike lives in a namespace and is
        // referenced unqualified from inside that namespace.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;

            class DelayedTargetValidation {}

            #[DelayedTargetValidation]
            #[\Override]
            function standalone(): void {}
            """, phpVersion: "8.5");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeTargetMismatch).Should().Be(1);
    }

    [Fact]
    public void Check_UserAttributeClass_OnEachTargetKind_DoesNotReport4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class MyAttr {}

            #[MyAttr]
            class C {
                #[MyAttr]
                public const int FOO = 1;

                #[MyAttr]
                public int $x = 0;

                #[MyAttr]
                public function go(): void {}
            }

            #[MyAttr]
            function free(): void {}

            #[MyAttr]
            const TOP = 1;

            enum E {
                #[MyAttr]
                case A;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerNotAnAttributeClass
            || d.Code == MessageCode.CheckerAttributeTargetMismatch);
    }

    // FOUND_BUGS #33: attributes on members of an anonymous class inside a method must resolve.
    [Fact]
    public void Check_UserAttributeOnAnonymousClassMemberInsideMethod_DoesNotReport4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class MyAttr {}

            class C {
                public function go(): void {
                    $obj = new class {
                        #[MyAttr]
                        public function inner(): void {}

                        #[MyAttr]
                        public int $x = 0;
                    };
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_AttributeMetaOnClass_DoesNotReport4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class MyAttr {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_NonRepeatableAttributeRepeated_Reports4128Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class MyAttr {}

            class C {
                #[MyAttr]
                #[MyAttr]
                public function go(): void {}
            }
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerAttributeNotRepeatable).Should().Be(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_RepeatableAttributeRepeated_DoesNotReport4128()
    {
        // IS_REPEATABLE alone sets no TARGET_* bits (PHP rejects every target on
        // ReflectionAttribute::newInstance). Pair it with an explicit target.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD | \Attribute::IS_REPEATABLE)]
            class MyAttr {}

            class C {
                #[MyAttr]
                #[MyAttr]
                public function go(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeNotRepeatable
            || d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_RepeatableAttributeWithTargetFlags_DoesNotReport4128()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD | \Attribute::IS_REPEATABLE)]
            class MyAttr {}

            class C {
                #[MyAttr]
                #[MyAttr]
                public function go(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeNotRepeatable
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Theory]
    // PHP 8.5: Attribute::IS_REPEATABLE is 128 (TARGET_CONSTANT took bit 64).
    // 132 = TARGET_METHOD|IS_REPEATABLE; 255 = TARGET_ALL|IS_REPEATABLE.
    // Pure 128 alone has no TARGET_* bits and mismatches every site (same as named IS_REPEATABLE).
    [InlineData("132")]
    [InlineData("255")]
    public void Check_NumericRepeatableFlag_DoesNotReport4128(string flags)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            #[\Attribute({{flags}})]
            class MyAttr {}

            class C {
                #[MyAttr]
                #[MyAttr]
                public function go(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeNotRepeatable
            || d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_AttributeNameSharedWithClassMember_DoesNotReport4126()
    {
        // An attribute names a class, so the enclosing class's own `marker()` / `const Marker`
        // must not end the lexical walk (PHP keeps members in their own symbol tables).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class Marker {}

            class C {
                public const int Marker = 1;

                #[Marker]
                public function go(): void {}

                public function marker(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_AttributeNameSharedWithFunction_DoesNotReport4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class Marker {}

            function marker(): void {}

            #[Marker]
            class C {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_ImportedAttributeClass_DoesNotReport4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Attrs;

            #[\Attribute]
            class MyAttr {}

            namespace App\Models;

            use App\Attrs\MyAttr;

            #[MyAttr]
            class C {
                #[MyAttr]
                public int $x = 0;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_TraitOrInterfaceUsedAsAttribute_Reports4126()
    {
        // Resolution must not make every bound name look like an attribute class.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {}
            interface I {}

            #[T]
            class C {}

            #[I]
            class D {}
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(2);
    }

    private static DiagnosticBag CompileAndCheck(string content, string phpVersion = "8.4")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: phpVersion, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics, symbolTree, result.GlobalScope!, options.Checker);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
