using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Block-target extension checker: header XOR nests, target shape, unused and
/// shadowed binders, intra-extension overlap, <c>static::</c> / <c>parent::</c>,
/// and the <c>&amp;$this</c> annotation.
/// </summary>
[Trait("Category", "Checker")]
public class ExtensionBlockTargetCheckerTests
{
    [Fact]
    public void EmptyExtension_StillReports4172()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension EmptyOps {
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerEmptyExtension);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionMissingExtends);
    }

    [Fact]
    public void HeaderExtension_DoesNotReport4147()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                function label(): self {
                    return $this;
                }
            }
            """);

        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void NestedGroups_DoNotReport4147()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension Ops {
                extends Money {
                    function label(): string { return "m"; }
                }
                extends string {
                    function len(): int { return 1; }
                }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionMissingExtends);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void MembersWithoutTarget_Report4147()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionMissingExtends);
    }

    [Fact]
    public void HeaderAndNestedGroups_ReportMix()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                function label(): string { return "m"; }
                extends string {
                    function len(): int { return 1; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionHeaderAndNestedTargets);
    }

    [Fact]
    public void LooseMemberBesideGroups_ReportsMix()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops {
                function loose(): int { return 1; }
                extends string {
                    function len(): int { return 1; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionLooseMemberBesideGroup);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionMissingExtends);
    }

    [Theory]
    [InlineData("void")]
    [InlineData("never")]
    [InlineData("mixed")]
    public void IllegalBuiltinTarget_Reports4364(string target)
    {
        var errors = SnippetErrors($$"""
            <?tyhp
            extension Ops extends {{target}} {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void UnionTarget_AppliesToEachClass_AndThisIsTheUnion()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Money { public int $amount = 0; }
            class Stamp { public int $unix = 0; }
            extension Ops extends Money|Stamp {
                function label(): string { return "x"; }
                function id(): Money|Stamp { return $this; }
            }
            function onMoney(Money $value): string { return $value->label(); }
            function onStamp(Stamp $value): string { return $value->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
        CallTypeInFunction(result, "onMoney", "label").Should().Be("string");
        CallTypeInFunction(result, "onStamp", "label").Should().Be("string");
        ThisTypes(result).Should().Contain(type => type.Contains("Money") && type.Contains("Stamp"));
    }

    [Fact]
    public void IntersectionTarget_Reports4364()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money { public int $amount = 0; }
            class Stamp { public int $unix = 0; }
            extension Ops extends Money&Stamp {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void UnionWithIntersectionMemberViaAlias_Reports4364()
    {
        // The intersection is buried inside one union member (reached through a type alias),
        // not written at the top of the target — every union member must itself be a legal
        // target, not just the first one.
        var errors = SnippetErrors("""
            <?tyhp
            class Money { public int $amount = 0; }
            class Stamp { public int $unix = 0; }
            type Combo = Money&Stamp;
            extension Ops extends Combo|string {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void UnionWithVoidMember_Reports4364()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops extends void|string {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void UnionWithGenericTypeParameterMember_Reports4364()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops<T> extends T|string {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void WildcardTarget_Reports4364()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops extends _ {
                function label(): string { return "x"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void UnusedExtensionTypeParameter_Reports4365()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money { public int $amount = 0; }
            extension MoneyOps<T> extends Money {
                function label(): string { return "m"; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionUnusedTypeParameter);
    }

    [Fact]
    public void UnusedGroupTypeParameter_Reports4365()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money { public int $amount = 0; }
            extension Ops {
                extends<T> Money {
                    function label(): string { return "m"; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionUnusedTypeParameter);
    }

    [Fact]
    public void TypeParameterUsedOnTarget_IsAllowed()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class MyClass<T> { public int $n = 0; }
            extension Ops<T> extends MyClass<T> {
                function label(): string { return "m"; }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionUnusedTypeParameter);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void MethodGenericShadowingBinder_Reports4366()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class MyClass<T> { public int $n = 0; }
            extension Ops<T> extends MyClass<T> {
                function map<T>(T $value): T { return $value; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTypeParameterShadowed);
    }

    [Fact]
    public void GroupMethodGenericShadowingBinder_Reports4366()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class MyClass<T> { public int $n = 0; }
            extension Ops {
                extends<T> MyClass<T> {
                    function map<T>(T $value): T { return $value; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionTypeParameterShadowed);
    }

    [Fact]
    public void ExtraMethodGeneric_IsAllowed()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class MyClass<T> { public int $n = 0; }
            extension Ops<T> extends MyClass<T> {
                function map<R>(R $value): R { return $value; }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTypeParameterShadowed);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void OverlappingOpenAndClosedGeneric_Reports4367()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class MyClass<T> { public int $n = 0; }
            extension Ops {
                extends MyClass<string> {
                    function label(): string { return "s"; }
                }
                extends<T> MyClass<T> {
                    function label(): string { return "t"; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionOverlappingMember);
    }

    [Fact]
    public void DisjointGenericConstraint_AllowsSameMethodName()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class MyClass<T> { public int $n = 0; }
            extension Ops {
                extends<T extends int> MyClass<T> {
                    function label(): string { return "i"; }
                }
                extends MyClass<string> {
                    function label(): string { return "s"; }
                }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionOverlappingMember);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void DistinctClasses_AllowSameMethodName()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money { public int $amount = 0; }
            class Stamp { public int $unix = 0; }
            extension Ops {
                extends Money {
                    function label(): string { return "m"; }
                }
                extends Stamp {
                    function label(): string { return "s"; }
                }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionOverlappingMember);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void SeparateExtensions_DoNotOverlap()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension A extends string {
                function label(): string { return "a"; }
            }
            extension B extends string {
                function label(): string { return "b"; }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionOverlappingMember);
    }

    [Fact]
    public void StaticAndParent_AreIllegal()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money {
                public const NAME = "m";
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                function name(): string { return static::NAME; }
                function parentName(): string { return parent::NAME; }
            }
            """);

        errors.Where(d => d.Code == MessageCode.CheckerExtensionRelativeType)
            .Should().HaveCount(2);
    }

    [Fact]
    public void StaticInsideAnonymousClass_IsTheAnonymousClass()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money { public int $amount = 0; }
            extension MoneyOps extends Money {
                function wrap(): object {
                    return new class {
                        public function name(): string { return static::class; }
                    };
                }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionRelativeType);
    }

    [Fact]
    public void WritingThisWithoutAnnotation_Reports4369()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension IntOps extends int {
                function bump(): void { $this = $this + 1; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void PassingThisByReferenceWithoutAnnotation_Reports4369()
    {
        var errors = SnippetErrors("""
            <?tyhp
            function writeInt(int &$value): void { $value = 1; }
            extension IntOps extends int {
                function bump(): void { writeInt($this); }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void AnnotationWithoutWrite_Reports4370()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension IntOps extends int {
                function bump(&$this): int { return $this; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverUnused);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void AnnotationWithWrite_IsAllowed()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension IntOps extends int {
                function bump(&$this): void { $this = $this + 1; }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverUnused);
    }

    [Fact]
    public void ObjectMethodMutation_DoesNotRequireByRefReceiver()
    {
        var errors = SnippetErrors("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function add(int $n): void { $this->amount += $n; }
            }
            extension MoneyOps extends Money {
                function bump(): void { $this->add(1); }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverUnused);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void ObjectTarget_DirectPropertyAssignment_DoesNotRequireByRefReceiver()
    {
        // Object handles are shared — mutating a property through $this is visible to the
        // caller without &$this, same as calling a mutating method (decision 4).
        var errors = SnippetErrors("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                function bump(): void { $this->amount = $this->amount + 1; }
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverUnused);
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void StructTarget_PropertyAssignmentWithoutAnnotation_Reports4369()
    {
        // Struct targets are by-value PHP parameters — writing through a $this-rooted member
        // only mutates the local copy, same as reassigning $this itself (decision 4).
        var errors = SnippetErrors("""
            <?tyhp
            type Money = struct {
                int $amount = 0;
            };
            extension MoneyOps extends Money {
                function bump(): void { $this->amount = $this->amount + 1; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void ArrayTarget_AppendWithoutAnnotation_Reports4369()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension ArrOps extends array {
                function push(int $v): void { $this[] = $v; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void CompoundAssignmentToThis_WithoutAnnotation_Reports4369()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension IntOps extends int {
                function bump(): void { $this += 1; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void ClassBodyTyhpdefExtensionFn_DoesNotReport4147()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            class Box {
                extension fn len(): int => 0;
            }
            """,
            skipChecking: false);

        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerExtensionMissingExtends);
    }

    [Fact]
    public void CallResult_Chain_DoesNotReport4197()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Box {
                public int $prop = 0;
            }
            extension BoxOps extends Box {
                function m(): Box {
                    return $this;
                }
            }
            function demo(Box $x): int {
                return $x->m()->prop;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerUnresolvedReceiver,
            Dump(errors) + " || " + DumpCallTypes(result));
        errors.Should().BeEmpty(Dump(errors));
        CallType(result, "m").Should().Be("\\App\\Box");
    }

    [Fact]
    public void CallResult_AssignThenUse_DoesNotReport4197()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Box {
                public int $prop = 0;
            }
            extension BoxOps extends Box {
                function m(): Box {
                    return $this;
                }
            }
            function demo(Box $x): int {
                $copy = $x->m();
                return $copy->prop;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerUnresolvedReceiver,
            Dump(errors) + " || " + DumpCallTypes(result));
        errors.Should().BeEmpty(Dump(errors));
        CallType(result, "m").Should().Be("\\App\\Box");
    }

    [Fact]
    public void CallResult_BareReturn_DoesNotReport4197()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Box {
                public int $prop = 0;
            }
            extension BoxOps extends Box {
                function m(): Box {
                    return $this;
                }
            }
            function demo(Box $x): Box {
                return $x->m();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        errors.Should().BeEmpty(Dump(errors));
        CallType(result, "m").Should().Be("\\App\\Box");
    }

    [Fact]
    public void SelfReturn_Chain_IsTargetType()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Box {
                public int $prop = 0;
            }
            extension BoxOps extends Box {
                function made(): self {
                    return $this;
                }
            }
            function demo(Box $x): int {
                return $x->made()->prop;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "made").Should().Be("\\App\\Box");
    }

    [Fact]
    public void ScalarCallResult_Chain_DoesNotReport4197()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            extension StrOps extends string {
                function doubled(): string {
                    return $this . $this;
                }
            }
            function demo(string $s): string {
                return $s->doubled()->doubled();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerUnresolvedReceiver,
            Dump(errors) + " || " + DumpCallTypes(result));
        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void NestedGroupCallResult_Chain_DoesNotReport4197()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Box {
                public int $prop = 0;
            }
            extension Ops {
                extends Box {
                    function m(): Box {
                        return $this;
                    }
                }
            }
            function demo(Box $x): int {
                return $x->m()->prop;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerUnresolvedReceiver,
            Dump(errors) + " || " + DumpCallTypes(result));
        errors.Should().BeEmpty(Dump(errors));
        CallType(result, "m").Should().Be("\\App\\Box");
    }

    [Fact]
    public void SelfReturn_ScalarTarget_IsTargetType()
    {
        // `self` on `extends string` names the builtin target, not the extension class —
        // ExtensionBlockTargetSymbol here is a BuiltInTypeSymbol, which cannot populate
        // CheckerState.EnclosingObject (ObjectDeclarationSymbol-typed).
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension StrOps extends string {
                function identity(): self {
                    return $this;
                }
            }
            function demo(string $s): string {
                return $s->identity();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "identity").Should().Be("string");
    }

    [Fact]
    public void TwoNamespacedExtensions_SameMethodName_DifferentTargets_DoNotCrossBind()
    {
        // The global-index fallback (`ExtensionMethodAppliesToReceiver` with `_globalScope`)
        // must still pick the extension whose block target matches the receiver, not just
        // the first same-named method it finds.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Box {
                public int $prop = 0;
            }
            class Crate {
                public int $prop2 = 0;
            }
            extension BoxOps extends Box {
                function m(): Box {
                    return $this;
                }
            }
            extension CrateOps extends Crate {
                function m(): Crate {
                    return $this;
                }
            }
            function demoBox(Box $x): int {
                return $x->m()->prop;
            }
            function demoCrate(Crate $y): int {
                return $y->m()->prop2;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
    }

    [Fact]
    public void AliasToNullableString_ThisIsNullable_AndBothReceiversSeeTheMethod()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type MaybeStr = ?string;
            extension StrOps extends MaybeStr {
                function label(): ?string { return $this; }
                function raw(): string { return $this; }
                function narrowed(): string {
                    if ($this !== null) {
                        return $this;
                    }
                    return "";
                }
            }
            function onString(string $s): ?string { return $s->label(); }
            function onNullable(?string $s): ?string { return $s->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
        errors.Should().NotContain(d => d.Code == MessageCode.ExtensionOperatorTargetNotFound);
        errors.Where(d => d.Code == MessageCode.CheckerIncompatibleReturnType)
            .Should().ContainSingle("raw() must reject $this before a null check: " + Dump(errors));
        CallType(result, "label").Should().Be("?string");
        ThisTypes(result).Should().Contain("?string");
    }

    [Fact]
    public void SelfReturn_NullableTarget_IsNullableTargetType()
    {
        // `self` on a nullable block target (`extends ?string`) must keep that nullability at
        // the call site — ResolveMethodReturnType used to seed EnclosingObjectType from the
        // unwrapped, non-nullable ExtensionBlockTargetSymbol, so `self` silently lost the `?`.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension StrOps extends ?string {
                function again(): self { return $this; }
            }
            function demo(?string $s): ?string {
                return $s->again();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "again").Should().Be("?string");
    }

    [Fact]
    public void SelfReturn_AliasToNullableTarget_IsNullableTargetType()
    {
        // Same bug via an alias of `?string` rather than a directly written `?string` target.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type MaybeStr = ?string;
            extension StrOps extends MaybeStr {
                function again(): self { return $this; }
            }
            function demo(?string $s): ?string {
                return $s->again();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "again").Should().Be("?string");
    }

    [Fact]
    public void OperatorSelfReturn_NullableTarget_IsNullableTargetType()
    {
        // Same nullability-dropping bug as SelfReturn_NullableTarget_IsNullableTargetType, but
        // for an extension-contributed operator's `self` return type
        // (TypeInferrer.ResolveOperatorOverloadReturnType's builtin-owner branch).
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension StrOps extends ?string {
                operator + (self $left, self $right): self { return $left; }
            }
            function demo(?string $s): ?string {
                return $s + $s;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors));
        BinaryOpType(result, PhpBinaryOperator.Plus).Should().Be("?string");
    }

    [Fact]
    public void AliasToUnion_IsTheUnion()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type Num = string|int;
            extension Ops extends Num {
                function label(): string { return "x"; }
                function id(): Num { return $this; }
            }
            function onString(string $value): string { return $value->label(); }
            function onInt(int $value): string { return $value->label(); }
            function onUnion(string|int $value): string { return $value->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
        CallTypeInFunction(result, "onString", "label").Should().Be("string");
        CallTypeInFunction(result, "onInt", "label").Should().Be("string");
        CallTypeInFunction(result, "onUnion", "label").Should().Be("string");
        ThisTypes(result).Should().Contain(type => type.Contains("string") && type.Contains("int"));
    }

    [Fact]
    public void StringIntUnion_AppliesToMembersAndItself()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends string|int {
                function label(): string { return "x"; }
                function id(): string|int { return $this; }
            }
            function onString(string $value): string { return $value->label(); }
            function onInt(int $value): string { return $value->label(); }
            function onUnion(string|int $value): string { return $value->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypeInFunction(result, "onString", "label").Should().Be("string");
        CallTypeInFunction(result, "onInt", "label").Should().Be("string");
        CallTypeInFunction(result, "onUnion", "label").Should().Be("string");
        ThisTypes(result).Should().Contain("string|int");
    }

    [Fact]
    public void StringIntUnion_DoesNotApplyToNullableOrWiderUnion()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends string|int {
                function label(): string { return "x"; }
            }
            function onNullable(?string $value): string { return $value->label(); }
            function onWider(string|int|bool $value): string { return $value->label(); }
            """);

        CallTypeInFunction(result, "onNullable", "label").Should().Be("unresolved");
        CallTypeInFunction(result, "onWider", "label").Should().Be("unresolved");
        SnippetErrorsOf(result).Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void UnionMethodName_CoexistsWithSameNamedMethodOnPlainClassTarget()
    {
        // "label" has a union-target candidate (Ops), which used to make call-site lookup
        // for *every* "label" call bypass the ordinary class-target resolution entirely.
        // MoneyOps's plain `Money` target must still resolve correctly for a Money receiver.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Money { public int $amount = 0; }
            extension MoneyOps extends Money {
                function label(): int { return 1; }
            }
            extension Ops extends string|int {
                function label(): string { return "x"; }
            }
            function onMoney(Money $m): int { return $m->label(); }
            function onString(string $s): string { return $s->label(); }
            function onInt(int $n): string { return $n->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypeInFunction(result, "onMoney", "label").Should().Be("int");
        CallTypeInFunction(result, "onString", "label").Should().Be("string");
        CallTypeInFunction(result, "onInt", "label").Should().Be("string");
    }

    [Fact]
    public void UnionMethodName_CalledThroughUseExtensionAlias_ResolvesNonFirstMember()
    {
        // `HasUnionTargetMethod` and `SymbolTree.ExtensionMethodIndex` are keyed by the
        // *declared* method name. A call site that only knows the `use extension { … }`
        // rename must still be recognized as a union-target lookup and matched against
        // every member — not just fail silently once the receiver isn't the union's first
        // member (here `int`, the second member of `string|int`).
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends string|int {
                function label(): string { return "x"; }
            }
            use extension Ops {
                Ops::label as description;
            }
            function onString(string $s): string { return $s->description(); }
            function onInt(int $n): string { return $n->description(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypeInFunction(result, "onString", "description").Should().Be("string");
        CallTypeInFunction(result, "onInt", "description").Should().Be("string");
    }

    [Fact]
    public void UnionMethodName_AliasedThenHidden_StaysUnresolved()
    {
        // `hide` records the *declared* name (`label`), and the ordinary resolver already
        // treats an alias as hidden whenever its target is hidden. The union-aware
        // fallback must agree: resolving `description` back to `label` before checking
        // `HasUnionTargetMethod` must not let the alias resurrect a hidden union method.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends string|int {
                function label(): string { return "x"; }
            }
            use extension Ops {
                Ops::label as description;
                Ops::label hide;
            }
            function onString(string $s): string { return $s->description(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void UnionMethodName_AliasToPlainClassMethod_NotStolenByUnionCandidate()
    {
        // "label" also has a union-target candidate elsewhere (`Ops`), so
        // `HasUnionTargetMethod` fires for the declared name reached through the
        // `describe` alias. The ordinary resolver inside the union-aware path must still
        // win: MoneyOps's plain `Money` target is the real answer, not a union scan.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Money { public int $amount = 0; }
            extension MoneyOps extends Money {
                function label(): int { return 1; }
            }
            extension Ops extends string|int {
                function label(): string { return "x"; }
            }
            use extension MoneyOps {
                MoneyOps::label as describe;
            }
            function onMoney(Money $m): int { return $m->describe(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypeInFunction(result, "onMoney", "describe").Should().Be("int");
    }

    [Fact]
    public void Alias_WithNoUnionCandidate_ResolvesThroughOrdinaryPath()
    {
        // No union-target method named "label" exists anywhere in this snippet, so
        // `HasUnionTargetMethod` on the declared name is false and the call must resolve
        // through the plain `NameResolver.ResolveExtensionMethod` path, unaffected by the
        // alias-to-declared-name lookup added for the union case.
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Money { public int $amount = 0; }
            extension MoneyOps extends Money {
                function label(): int { return 1; }
            }
            use extension MoneyOps {
                MoneyOps::label as describe;
            }
            function onMoney(Money $m): int { return $m->describe(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypeInFunction(result, "onMoney", "describe").Should().Be("int");
    }

    [Fact]
    public void StringIntUnion_ThisStaysTheUnion()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops extends string|int {
                function raw(): string { return $this; }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void NullStringIntBoolUnion_AppliesToCombinationsAndNullables()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends null|string|int|bool {
                function label(): string { return "x"; }
            }
            function onString(string $value): string { return $value->label(); }
            function onInt(int $value): string { return $value->label(); }
            function onBool(bool $value): string { return $value->label(); }
            function onPair(string|int $value): string { return $value->label(); }
            function onSkipped(string|bool $value): string { return $value->label(); }
            function onThree(string|int|bool $value): string { return $value->label(); }
            function onFull(null|string|int|bool $value): string { return $value->label(); }
            function onNullableString(?string $value): string { return $value->label(); }
            function onNullableInt(?int $value): string { return $value->label(); }
            function onNullableBool(?bool $value): string { return $value->label(); }
            function onNullPair(null|string|int $value): string { return $value->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypeInFunction(result, "onString", "label").Should().Be("string");
        CallTypeInFunction(result, "onInt", "label").Should().Be("string");
        CallTypeInFunction(result, "onBool", "label").Should().Be("string");
        CallTypeInFunction(result, "onPair", "label").Should().Be("string");
        CallTypeInFunction(result, "onSkipped", "label").Should().Be("string");
        CallTypeInFunction(result, "onThree", "label").Should().Be("string");
        CallTypeInFunction(result, "onFull", "label").Should().Be("string");
        CallTypeInFunction(result, "onNullableString", "label").Should().Be("string");
        CallTypeInFunction(result, "onNullableInt", "label").Should().Be("string");
        CallTypeInFunction(result, "onNullableBool", "label").Should().Be("string");
        CallTypeInFunction(result, "onNullPair", "label").Should().Be("string");
    }

    [Fact]
    public void NullStringIntBoolUnion_DoesNotApplyToBareNull()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends null|string|int|bool {
                function label(): string { return "x"; }
            }
            function onNull(null $value): string { return $value->label(); }
            """);

        CallTypeInFunction(result, "onNull", "label").Should().Be("unresolved");
    }

    [Fact]
    public void StringAndStringInt_SameMethod_ReportsOverlap()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops {
                extends string {
                    function label(): string { return "s"; }
                }
                extends string|int {
                    function label(): string { return "u"; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionOverlappingMember);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void StringAndStringInt_DifferentMethods_DoNotOverlap()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops {
                extends string {
                    function fromString(): string { return "s"; }
                }
                extends string|int {
                    function fromUnion(): string { return "u"; }
                }
            }
            """);

        errors.Should().BeEmpty(Dump(errors));
    }

    [Fact]
    public void NullableStringTarget_AppliesToStringAndNullable_AndBodyMustHandleNull()
    {
        var rejected = SnippetErrors("""
            <?tyhp
            extension Ops extends ?string {
                function raw(): string { return $this; }
            }
            """);
        rejected.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        rejected.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);

        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            extension Ops extends ?string {
                function label(): ?string { return $this; }
                function narrowed(): string {
                    if ($this !== null) {
                        return $this;
                    }
                    return "";
                }
            }
            function onString(string $s): ?string { return $s->label(); }
            function onNullable(?string $s): ?string { return $s->label(); }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "label").Should().Be("?string");
        ThisTypes(result).Should().Contain("?string").And.Contain("string");
    }

    [Theory]
    [InlineData("object", "object")]
    [InlineData("callable", "callable")]
    [InlineData("(struct)", "struct")]
    public void ShapeTarget_IsAccepted_AndThisIsThatShape(string target, string thisType)
    {
        var result = IsolatedCompilation.ParseSnippet($$"""
            <?tyhp
            extension Ops extends {{target}} {
                function id(): {{target}} { return $this; }
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors));
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
        ThisTypes(result).Should().Contain(thisType);
    }

    [Fact]
    public void StringAndNullableString_SameMethod_ReportsOverlap()
    {
        var errors = SnippetErrors("""
            <?tyhp
            extension Ops {
                extends string {
                    function label(): string { return "s"; }
                }
                extends ?string {
                    function label(): string { return "n"; }
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionOverlappingMember);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionTargetNotSingleType);
    }

    [Fact]
    public void UnresolvedReceiver_StillReports4197()
    {
        var errors = SnippetErrors("""
            <?tyhp
            function demo(): int {
                return $hole->prop;
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
    }

    private static List<IDiagnostic> SnippetErrors(string source)
    {
        return SnippetErrorsOf(IsolatedCompilation.ParseSnippet(source, skipChecking: false));
    }

    private static List<IDiagnostic> SnippetErrorsOf(CompilationResult result)
    {
        return result.Diagnostics.Errors
            .Where(error => error.FileName.EndsWith("snippet.tyhp", StringComparison.Ordinal))
            .ToList();
    }

    private static string DumpCallTypes(CompilationResult result)
    {
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return "(no expression types)";
        }

        var calls = new List<string>();
        foreach (var file in result.ParsedFiles)
        {
            foreach (var deref in FindDerefs(file))
            {
                if (deref.Suffix is not PhpCallAst)
                {
                    continue;
                }

                calls.Add($"{CallMethodName(deref) ?? "?"}={TypeName(result, deref)}");
            }
        }

        return calls.Count == 0 ? "(no call types)" : string.Join("; ", calls);
    }

    private static string CallTypeInFunction(
        CompilationResult result,
        string functionName,
        string methodName)
    {
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return "<missing>";
        }

        foreach (var file in result.ParsedFiles)
        {
            foreach (var function in FindFunctions(file))
            {
                if (!string.Equals(function.Identifier, functionName, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var deref in FindDerefs(function))
                {
                    if (deref.Suffix is not PhpCallAst)
                    {
                        continue;
                    }

                    if (!string.Equals(CallMethodName(deref), methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return TypeName(result, deref);
                }
            }
        }

        return "<missing>";
    }

    private static IEnumerable<PhpFunctionDeclAst> FindFunctions(IBase2Ast root)
    {
        if (root is PhpFunctionDeclAst function)
        {
            yield return function;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindFunctions(child))
            {
                yield return nested;
            }
        }
    }

    private static string CallType(CompilationResult result, string methodName)
    {
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return "<missing>";
        }

        foreach (var file in result.ParsedFiles)
        {
            foreach (var deref in FindDerefs(file))
            {
                if (deref.Suffix is not PhpCallAst)
                {
                    continue;
                }

                if (!string.Equals(CallMethodName(deref), methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                return TypeName(result, deref);
            }
        }

        return "<missing>";
    }

    private static string BinaryOpType(CompilationResult result, PhpBinaryOperator op)
    {
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return "<missing>";
        }

        foreach (var file in result.ParsedFiles)
        {
            foreach (var binary in FindBinaryOps(file))
            {
                var token = binary.Operator?.TokenValue ?? -1;
                if (PhpBinaryOperatorExtensions.FromToken(token) != op)
                {
                    continue;
                }

                return result.ExpressionTypes.TryGetValue(binary, out var cached)
                    ? cached.DisplayName
                    : "<uncached>";
            }
        }

        return "<missing>";
    }

    private static IEnumerable<PhpBinaryOpAst> FindBinaryOps(IBase2Ast root)
    {
        if (root is PhpBinaryOpAst binary)
        {
            yield return binary;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindBinaryOps(child))
            {
                yield return nested;
            }
        }
    }

    private static string TypeName(CompilationResult result, PhpDereferenceableAst deref) =>
        result.ExpressionTypes!.TryGetValue(deref, out var cached)
            ? cached.DisplayName
            : "<uncached>";

    private static string? CallMethodName(PhpDereferenceableAst callDeref)
    {
        if (callDeref.Base is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst member })
        {
            return null;
        }

        return member.MemberName switch
        {
            PhpNameAst name => name.ValueString ?? name.Identifier,
            TokenValueAst token => token.ValueString,
            IExpression expression => expression.Identifier,
            _ => member.MemberName?.Identifier,
        };
    }

    private static IEnumerable<PhpDereferenceableAst> FindDerefs(IBase2Ast root)
    {
        if (root is PhpDereferenceableAst deref)
        {
            yield return deref;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindDerefs(child))
            {
                yield return nested;
            }
        }
    }

    private static string Dump(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(error => $"{(int)error.Code} {error.Message}"));

    private static IReadOnlyList<string> ThisTypes(CompilationResult result)
    {
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return [];
        }

        var types = new List<string>();
        foreach (var file in result.ParsedFiles)
        {
            foreach (var variable in FindVariables(file))
            {
                var name = variable.VariableToken?.ValueString ?? variable.Identifier;
                if (!string.Equals(name, "$this", StringComparison.Ordinal)
                    && !string.Equals(name, "this", StringComparison.Ordinal))
                {
                    continue;
                }

                if (result.ExpressionTypes.TryGetValue(variable, out var type) && type is not null)
                {
                    types.Add(type.DisplayName);
                }
            }
        }

        return types;
    }

    private static IEnumerable<PhpVariableAst> FindVariables(IBase2Ast root)
    {
        if (root is PhpVariableAst variable)
        {
            yield return variable;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindVariables(child))
            {
                yield return nested;
            }
        }
    }
}
