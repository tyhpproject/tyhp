using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class PropertyInitializationRuleTests
{
    [Fact]
    public void Check_UninitializedTypedPropertyRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_PropertyWithInitializer_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value = 0;
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_PromotedProperty_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function __construct(private int $value) {}
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AssignedInConstructor_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    $this->value = $v;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AssignedOnlyInOneConstructorBranch_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(bool $cond, int $v) {
                    if ($cond) {
                        $this->value = $v;
                    }
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AssignedInBothConstructorBranches_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(bool $cond, int $v) {
                    if ($cond) {
                        $this->value = $v;
                    } else {
                        $this->value = 0;
                    }
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_ReadBeforeAssignInConstructor_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    int $x = $this->value;
                    $this->value = $v;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_NullCoalesceGuardsUninitializedRead_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function get(): int {
                    return $this->value ?? 0;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_NullableWithoutDefault_StillReports4157()
    {
        // PHP: `?int $x` without `= null` is still uninitialized until assigned.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private ?int $value;
                public function get(): ?int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_NullableWithNullDefault_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private ?int $value = null;
                public function get(): ?int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_HelperAssignmentInConstructor_DoesNotCount()
    {
        // Decision: assignment via constructor-called helper does not count (intraprocedural).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    $this->init($v);
                }
                private function init(int $v): void {
                    $this->value = $v;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_SimpleAssignWriteTarget_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    $this->value = $v;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_CompoundAssignOnUninitializedProperty_Reports4157()
    {
        // `+=` (and other read-then-write compound operators) read the property before writing it,
        // so an uninitialized target still throws in PHP — unlike plain `=` / `??=`, this must NOT
        // be treated as an initializing write (regression: it previously marked the property
        // definitely-initialized before the implicit read was checked, suppressing 4157).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct() {
                    $this->value += 1;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_CompoundAssignOnAlreadyInitializedProperty_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value = 0;
                public function __construct() {
                    $this->value += 1;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_InheritedPropertyWithInitializer_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value = 0;
            }
            class Derived extends Base {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_SubclassConstructorInitializesInheritedProperty_DoesNotReport4157()
    {
        // Derived ctor assigns an inherited slot that Base left uninitialized. Credit must be
        // recorded on Derived without mutating Base's shared MayBeUninitializedAfterConstruction.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Derived extends Base {
                public function __construct(int $v) {
                    $this->value = $v;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_InheritedUninitializedProperty_UnguardedRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Derived extends Base {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_GrandchildNoOwnCtor_InheritsInitializingMiddleConstructor_DoesNotReport4157()
    {
        // Leaf declares no constructor of its own, so `new Leaf()` runs Middle's inherited
        // constructor at runtime. The credit Middle earns for initializing Base's `$value` must
        // propagate down to Leaf (and any further descendant without its own constructor).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Middle extends Base {
                public function __construct(int $v) {
                    $this->value = $v;
                }
            }
            class Leaf extends Middle {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_GreatGrandchildNoOwnCtorChain_InheritsInitializingConstructor_DoesNotReport4157()
    {
        // Four levels, with two consecutive descendants (Leaf, Sprout) declaring no constructor of
        // their own — the credit must chain through each level, not just skip one.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Middle extends Base {
                public function __construct(int $v) {
                    $this->value = $v;
                }
            }
            class Leaf extends Middle {
            }
            class Sprout extends Leaf {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_GrandchildNoOwnCtor_MiddleLeavesPropertyUninitialized_StillReports4157()
    {
        // Middle has its own constructor but never assigns `$value` — Leaf (no own constructor)
        // must still be flagged; the delegation must not become unconditionally permissive.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Middle extends Base {
                public function __construct() {
                }
            }
            class Leaf extends Middle {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_PrivateInheritedProperty_NotTrackedOnSubclass()
    {
        // Base's private $value is not visible from Derived; Derived's get() must not inherit a
        // spurious PropertyInit entry that would affect unrelated diagnostics.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private int $value;
                public function getBase(): int {
                    return $this->value;
                }
            }
            class Derived extends Base {
                public function get(): int {
                    return 0;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_StaticPropertyWithoutInitializer_Reports4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static string $x;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$x`", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_StaticPropertyWithInitializer_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static string $x = '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_NullableStaticPropertyWithNullInitializer_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static ?string $x = null;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_NullableStaticPropertyWithoutInitializer_Reports4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static ?string $x;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer);
    }

    [Fact]
    public void Check_StaticPropertyList_ReportsOnlyTheNameWithoutAnInitializer()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static string $a, $b = 'b';
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$a`", StringComparison.Ordinal));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$b`", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TraitStaticPropertyWithoutInitializer_Reports4418OnTheTrait()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counter {
                public static int $n;
            }
            class Uses {
                use Counter;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$n`", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TraitStaticPropertyWithInitializer_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counter {
                public static int $n = 0;
            }
            class Uses {
                use Counter;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_AbstractClassStaticPropertyWithoutInitializer_Reports4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class Base {
                protected static string $label;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$label`", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_InstancePropertyWithoutInitializer_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public string $name;
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer);
    }

    [Fact]
    public void Check_LocalWithoutInitializer_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $s;
                echo $s;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer);
    }

    [Fact]
    public void Check_InterfaceStaticProperty_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Bag {
                public static string $name;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer);
    }

    [Fact]
    public void Check_EnumStaticProperty_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Color {
                case Red;
                public static string $name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerEnumPropertyNotAllowed);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer);
    }

    [Fact]
    public void Check_ReadonlyClassStaticProperty_DoesNotReport4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Registry {
                public static int $count;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerReadonlyClassStaticProperty);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer);
    }

    [Fact]
    public void Check_AnonymousClassStaticPropertyWithoutInitializer_Reports4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): object {
                return new class {
                    public static string $x;
                };
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$x`", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_StaticPropertyWithoutInitializer_InNestedNamespaceClass_Reports4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            final class Config {
                private static array<string, string> $values;
                public static mixed $anything;
            }
            """);

        diagnostics.Errors.Where(d => d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer)
            .Should().HaveCount(2, Describe(diagnostics));
    }

    [Fact]
    public void Check_TyhpdefSourceFileWalkedByChecker_StaticPropertyWithoutInitializer_DoesNotReport4418()
    {
        // A tyhpdef file handed to the checker as a source file describes existing PHP, where a
        // typed static may have no default. The same declaration in a `.tyhp` file is TYHP4418.
        var diagnostics = CompileAndCheck("""
            <?tyhpdef
            class Money {
                public static int $rate;
                public static ?string $label;
            }
            trait Counter {
                public static int $n;
            }
            """, "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer, Describe(diagnostics));
    }

    [Fact]
    public void Check_ReplacedConstructor_WithoutParentCallOrAssignment_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function __construct() {
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal)
            && d.Message.Contains("`Base`", StringComparison.Ordinal),
            Describe(diagnostics));
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_DominatingParentConstructorCall_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function __construct() {
                    parent::__construct();
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_ParentDelegationSyntax_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function __construct(): parent() {}
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_ParentConstructorCallOnOneBranchOnly_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function __construct(bool $cond) {
                    if ($cond) {
                        parent::__construct();
                    }
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_ParentConstructorCallOnBothBranches_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function __construct(bool $cond) {
                    if ($cond) {
                        parent::__construct();
                    } else {
                        parent::__construct();
                    }
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_InheritedDeclarationInitializer_WithoutParentCall_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name = "a";
            }
            class Child extends Base {
                public function __construct() {
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_InheritedPromotedProperty_SkippedParentConstructor_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function __construct(public string $name) {}
            }
            class Child extends Base {
                public function __construct() {
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal),
            Describe(diagnostics));
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_InheritedPromotedProperty_NoChildConstructor_DoesNotReport4419()
    {
        // The child declares no constructor, so PHP runs Base's constructor and the
        // promotion writes `$name`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function __construct(public string $name) {}
            }
            class Child extends Base {
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_SubclassWithoutConstructor_InheritsParentBodyInit_DoesNotReport4419()
    {
        // No constructor on Child means PHP runs Base::__construct.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_DirectAssignmentInitializesInheritedProperty_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            class Child extends Base {
                public function __construct(string $name) {
                    $this->name = $name;
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_HelperDoesNotInitializeInheritedProperty_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $name;
            }
            class Child extends Base {
                public function __construct(string $name) {
                    $this->init($name);
                }
                private function init(string $name): void {
                    $this->name = $name;
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_TraitPropertyWithoutInitializer_Reports4419OnUsingClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Named {
                public string $name;
            }
            class Box {
                use Named;
                public function __construct() {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal)
            && d.Message.Contains("`Named`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_TraitPropertyWithInitializer_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Named {
                public string $name = "a";
            }
            class Box {
                use Named;
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_TraitPropertyOnSubclass_MustBeInitializedByUsingClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $label = "l";
            }
            trait Named {
                public string $name;
            }
            class Box extends Base {
                use Named;
                public function __construct() {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal),
            Describe(diagnostics));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$label`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_TraitPropertyAssignedInUsingConstructor_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
            }
            trait Named {
                public string $name;
            }
            class Box extends Base {
                use Named;
                public function __construct() {
                    $this->name = "a";
                }
                public function get(): string {
                    return $this->name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_PrivateAncestorProperty_RequiresParentConstructor_NotChildAssignment()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $secret;
                public string $label;
                public function __construct() {
                    $this->secret = "s";
                    $this->label = "l";
                }
            }
            class Child extends Base {
                public function __construct() {
                    $this->label = "c";
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$secret`", StringComparison.Ordinal)
            && d.Message.Contains("`Base`", StringComparison.Ordinal),
            Describe(diagnostics));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$label`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_PrivateAncestorProperty_DominatingParentCall_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $secret;
                public function __construct() {
                    $this->secret = "s";
                }
                public function reveal(): string {
                    return $this->secret;
                }
            }
            class Child extends Base {
                public function __construct() {
                    parent::__construct();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_PrivateAncestorWithInitializer_DoesNotRequireParentCall()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $secret = "s";
                public function reveal(): string {
                    return $this->secret;
                }
            }
            class Child extends Base {
                public function __construct() {
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_ChildDeclaredBeforeParent_ParentCallStillCreditsBodyInit()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Child extends Base {
                public function __construct() {
                    parent::__construct();
                }
                public function get(): string {
                    return $this->name;
                }
            }
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor,
            Describe(diagnostics));
    }

    [Fact]
    public void Check_ChildDeclaredBeforeParent_SkippedParentCall_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Child extends Base {
                public function __construct() {
                }
            }
            class Base {
                public string $name;
                public function __construct() {
                    $this->name = "a";
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_PromotedParameterDefault_IsNotADeclarationInitializerAcrossReplace()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function __construct(public string $name = "a") {}
            }
            class Child extends Base {
                public function __construct() {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$name`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    private const string BaseWithConstructor = """
        class Base {
            public string $name;
            public function __construct() { $this->name = "a"; }
        }
        """;

    private static bool Reports4419(DiagnosticBag diagnostics, string property) =>
        diagnostics.Errors.Any(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains($"`${property}`", StringComparison.Ordinal));

    [Fact]
    public void Check_ReplacedConstructor_EarlyReturnBeforeParentCall_Reports4419()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public function __construct(bool $skip) {
                    if ($skip) { return; }
                    parent::__construct();
                }
            }
            """);

        Reports4419(diagnostics, "name").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_EarlyReturnBeforeDirectAssignment_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public string $n;
                public function __construct(bool $skip) {
                    if ($skip) { return; }
                    $this->n = "a";
                }
            }
            """);

        Reports4419(diagnostics, "n").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_ReturnAfterParentCall_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public function __construct(bool $skip) {
                    parent::__construct();
                    if ($skip) { return; }
                }
            }
            """);

        Reports4419(diagnostics, "name").Should().BeFalse(Describe(diagnostics));
    }

    [Fact]
    public void Check_ThrowBeforeParentCall_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public function __construct(bool $bad) {
                    if ($bad) { throw new \Exception("bad"); }
                    parent::__construct();
                }
            }
            """);

        Reports4419(diagnostics, "name").Should().BeFalse(Describe(diagnostics));
    }

    [Fact]
    public void Check_ParentCallInsideClosure_DoesNotCountAsDominating()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public function __construct() {
                    $f = function (): void { parent::__construct(); };
                    $f();
                }
            }
            """);

        Reports4419(diagnostics, "name").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_ParentCallInsideLoop_DoesNotCountAsDominating()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public function __construct(bool $again) {
                    while ($again) {
                        parent::__construct();
                        $again = false;
                    }
                }
            }
            """);

        Reports4419(diagnostics, "name").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_NoConstructorChain_ReportsInheritedGapOnceOnTheClassThatCausesIt()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Mid extends Base {
                public function __construct() { }
            }
            class Leaf extends Mid { }
            """);

        diagnostics.Errors.Count(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor).Should().Be(1, Describe(diagnostics));
    }

    [Fact]
    public void Check_AnonymousClassReplacedConstructor_DiagnosticNamesTheDeclaringBase()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            function make(): object {
                return new class extends Base { public function __construct() { } };
            }
            """);

        Reports4419(diagnostics, "name").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_AnonymousClassUninitializedProperty_NamesClassAnonymous()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function make(): object {
                return new class { public string $x; };
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`class@anonymous`", StringComparison.Ordinal), Describe(diagnostics));
    }

    [Fact]
    public void Check_HookedAndStaticPropertiesWithoutStorage_DoNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public string $x { get => "a"; }
                public static string $s = "";
                public int $n = 0;
                public function __construct() { }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_OnlySomePropertiesInitialized_ReportsTheRest()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public string $a;
                public string $b;
                public function __construct() { $this->a = "a"; }
            }
            """);

        Reports4419(diagnostics, "b").Should().BeTrue(Describe(diagnostics));
        Reports4419(diagnostics, "a").Should().BeFalse(Describe(diagnostics));
    }

    [Fact]
    public void Check_TraitConstructor_UsedByClassWithoutConstructor_InitializesProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Named {
                public string $name;
                public function __construct() { $this->name = "a"; }
            }
            class User { use Named; }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_TraitPromotedConstructor_UsedByClassWithoutConstructor_InitializesProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Named {
                public function __construct(public string $name) { }
            }
            class User { use Named; }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_TyhpdefParentWithoutConstructor_NothingToInitialize()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends NativeStmt {
                public function __construct() { }
                public function g(): string { return $this->queryString; }
            }
            class B extends NativeStmt { }
            """, stub: TyhpdefStub);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_TyhpdefParentConstructor_ParentCallCreditsStubProperties()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends WithCtor {
                public function __construct() { parent::__construct(); }
            }
            class B extends WithCtor { }
            class C extends Mid { }
            """, stub: TyhpdefStub);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_TyhpdefParentConstructor_ReplacedWithoutParentCall_Reports4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends WithCtor {
                public function __construct() { }
            }
            """, stub: TyhpdefStub);

        Reports4419(diagnostics, "a").Should().BeTrue(Describe(diagnostics));
        Reports4419(diagnostics, "n").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_TyhpdefParentWithInitializedProperty_ReplacedConstructor_DoesNotReport4419()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A extends WithInit {
                public function __construct() { }
            }
            """, stub: TyhpdefStub);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsPropertyAssignedOnEveryConstructorPath_DoesNotReport4157()
    {
        // The hook is declared before the constructor. Construction facts are recorded
        // before the hook body is checked, so source order does not matter.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $virtual {
                    get => $this->free;
                }
                public function __construct(int $free): void {
                    $this->free = $free;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized, Describe(diagnostics));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsPropertyConstructorDoesNotAssign_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $virtual {
                    get => $this->free;
                }
                public function __construct(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            && d.Message.Contains("`$free`", StringComparison.Ordinal),
            Describe(diagnostics));
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$free`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsPropertyAssignedOnOneConstructorPath_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $virtual {
                    get => $this->free;
                }
                public function __construct(bool $cond, int $free): void {
                    if ($cond) {
                        $this->free = $free;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            && d.Message.Contains("`$free`", StringComparison.Ordinal),
            Describe(diagnostics));
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            && d.Message.Contains("`$free`", StringComparison.Ordinal),
            Describe(diagnostics));
    }

    [Fact]
    public void Check_PromotedHookReadsPropertyAssignedInConstructor_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public function __construct(
                    public int $label {
                        get => $this->free;
                    },
                    int $free
                ): void {
                    $this->free = $free;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized, Describe(diagnostics));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor, Describe(diagnostics));
    }

    private static bool Reports4157(DiagnosticBag diagnostics, string property) =>
        diagnostics.Errors.Any(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            && d.Message.Contains($"`${property}`", StringComparison.Ordinal));

    [Fact]
    public void Check_HookReadsPropertyAssignedAfterEarlyReturn_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $virtual {
                    get => $this->free;
                }
                public function __construct(bool $skip, int $free): void {
                    if ($skip) { return; }
                    $this->free = $free;
                }
            }
            """);

        Reports4157(diagnostics, "free").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsInheritedPropertyInitializedByInheritedConstructor_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public string $upper {
                    get => \strtoupper($this->name);
                }
            }
            """);

        Reports4157(diagnostics, "name").Should().BeFalse(Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsInheritedPropertyWithParentCallOnEveryPath_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public string $upper {
                    get => \strtoupper($this->name);
                }
                public function __construct() {
                    parent::__construct();
                }
            }
            """);

        Reports4157(diagnostics, "name").Should().BeFalse(Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsInheritedPropertyWithoutParentCall_Reports4157()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public string $upper {
                    get => \strtoupper($this->name);
                }
                public function __construct() { }
            }
            """);

        Reports4157(diagnostics, "name").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_HookReadsInheritedPropertyWithParentCallBeforeEarlyReturnOnly_Reports4157()
    {
        var diagnostics = CompileAndCheck("<?tyhp\n" + BaseWithConstructor + """

            class Child extends Base {
                public string $upper {
                    get => \strtoupper($this->name);
                }
                public function __construct(bool $skip) {
                    if ($skip) { return; }
                    parent::__construct();
                }
            }
            """);

        Reports4157(diagnostics, "name").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_TraitConstructorHook_UsesTraitConstructorFacts()
    {
        var assigned = CompileAndCheck("""
            <?tyhp
            trait Named {
                public string $name;
                public function __construct(string $name) { $this->name = $name; }
            }
            class Box {
                use Named;
                public string $upper {
                    get => \strtoupper($this->name);
                }
            }
            """);

        Reports4157(assigned, "name").Should().BeFalse(Describe(assigned));

        var skipped = CompileAndCheck("""
            <?tyhp
            trait Named {
                public string $name;
                public function __construct(bool $skip, string $name) {
                    if ($skip) { return; }
                    $this->name = $name;
                }
            }
            class Box {
                use Named;
                public string $upper {
                    get => \strtoupper($this->name);
                }
            }
            """);

        Reports4157(skipped, "name").Should().BeTrue(Describe(skipped));
    }

    [Fact]
    public void Check_ConstructorReadsHookedPropertyBeforeHookDependencyAssigned_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $virtual {
                    get => $this->free;
                }
                public function __construct(int $free): void {
                    $copy = $this->virtual;
                    $this->free = $free;
                }
            }
            """);

        Reports4157(diagnostics, "free").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_ConstructorWritesHookedPropertyBeforeHookDependencyAssigned_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $label {
                    set {
                        $this->label = $value + $this->free;
                    }
                }
                public function __construct(int $free): void {
                    $this->label = 1;
                    $this->free = $free;
                }
            }
            """);

        Reports4157(diagnostics, "free").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_ConstructorTouchesHookedPropertyAfterHookDependencyAssigned_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public int $label {
                    set {
                        $this->label = $value + $this->free;
                    }
                }
                public int $virtual {
                    get => $this->free;
                }
                public function __construct(int $free): void {
                    $this->free = $free;
                    $this->label = 1;
                    $copy = $this->virtual;
                }
            }
            """);

        Reports4157(diagnostics, "free").Should().BeFalse(Describe(diagnostics));
    }

    [Fact]
    public void Check_PromotedSetHookReadsPropertyAssignedInConstructorBody_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $free;
                public function __construct(
                    public int $label {
                        set {
                            $this->label = $value + $this->free;
                        }
                    },
                    int $free
                ): void {
                    $this->free = $free;
                }
            }
            """);

        Reports4157(diagnostics, "free").Should().BeTrue(Describe(diagnostics));
    }

    [Fact]
    public void Check_HookedStaticAndInstanceProperties_StaticPropertyIsNotInstanceInitialization()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static int $count = 0;
                public int $virtual {
                    get => self::$count;
                }
                public function __construct() { }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyPossiblyUninitialized
            || d.Code == MessageCode.CheckerPropertyNotInitializedByConstructor
            || d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer, Describe(diagnostics));
    }

    private const string TyhpdefStub = """
        <?tyhpdef
        class NativeStmt { public string $queryString; public function q(): string; }
        class WithCtor { public string $a; protected int $n; public function __construct(): void; }
        class Mid extends WithCtor { }
        class WithInit { public string $a ?? ''; }
        """;

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"));

    private static DiagnosticBag CompileAndCheck(string content, string fileName = "test.tyhp", string? stub = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);
        var files = new List<string>();
        if (stub is not null)
        {
            var stubPath = Path.Combine(tempDir, "stub.tyhpdef");
            File.WriteAllText(stubPath, stub);
            files.Add(stubPath);
        }

        files.Add(filePath);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles(files, options);
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
