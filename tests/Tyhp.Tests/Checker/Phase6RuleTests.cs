using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase6RuleTests
{
    [Fact]
    public void Check_AwaitOutsideAsync_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                await loadAsync();
            }

            async function loadAsync(): int { return 1; }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInReturnOutsideAsync_ReportsError()
    {
        // ControlFlowRule suppresses child traversal on return statements; await inside
        // `return await …` must still be validated via the expression-tree walk.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            function fetchDataSync(int $id): string {
                return await fetchRawAsync($id);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInAsyncReturn_DoesNotReportOutsideAsync()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            async function fetchDataAsync(int $id): string {
                return await fetchRawAsync($id);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInAsyncMethodReturn_DoesNotReportOutsideAsync()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Loader {
                async function fetchRawAsync(int $id): string {
                    return "id-" . $id;
                }

                async function fetchDataAsync(int $id): string {
                    return await $this->fetchRawAsync($id);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInSyncMethodReturn_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Loader {
                async function fetchRawAsync(int $id): string {
                    return "id-" . $id;
                }

                function fetchDataSync(int $id): string {
                    return await $this->fetchRawAsync($id);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_VariableVariable_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $name = 'value';
                mixed $x = $$name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariableVariableProhibited);
    }

    [Fact]
    public void Check_CompactCall_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $a = 'hello';
                array $data = compact('a');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCompactProhibited);
    }

    [Fact]
    public void Check_AssignmentInCondition_ReportsWarning()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): bool {
                if ($x = true) {
                    return true;
                }
                return false;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerAssignmentInCondition);
    }

    [Fact]
    public void Check_WithInvalidProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }

            function demo(): void {
                Config $cfg = new Config();
                Config $copy = clone $cfg with [missing => true];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty);
    }

    [Fact]
    public void Check_WithInheritedProperty_IsAllowed()
    {
        // Inherited members are not flattened into Members; the rule must walk the base chain.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type SerializedExpression = struct {
                string $nodeType = '';
            };

            type SerializedParameterExpression = struct extends SerializedExpression  {
                string $name = '';
            };

            function demo(): SerializedParameterExpression {
                return new SerializedParameterExpression() with [
                    nodeType => 'parameter',
                    name => 'id',
                ];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty);
    }

    [Fact]
    public void Check_NewStructWith_MissingRequiredProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct {
                int $x;
                int $y = 0;
            };

            function demo(): Point {
                return new Point() with [y => 1];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_NewStructWith_RequiredPropertyProvided_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct {
                int $x;
                int $y = 0;
            };

            function demo(): Point {
                return new Point() with [x => 1];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_BareNewStruct_MissingRequiredProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct {
                int $x;
            };

            function demo(): Point {
                return new Point();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_StructPropertyType_UseImportInDeclaringFile_ResolvesAtAccessSite()
    {
        // Regression: PathNode-style structs declare property types via `use` in their own file.
        // Reading `$node->body` from another namespace must resolve that annotation against the
        // declaring file's imports — not the access site's namespace (which would yield
        // `\App\Node` instead of `\Lib\Node` and a TYHP4008 mismatch).
        var diagnostics = CompileAndCheckFiles(
            ("Node.tyhp", """
                <?tyhp
                namespace Lib;

                class Node {}
                """),
            ("Holder.tyhp", """
                <?tyhp
                namespace Lib\Structs;

                use Lib\Node;

                type Holder = struct {
                    Node $body;
                };
                """),
            ("Consumer.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Node;
                use Lib\Structs\Holder;

                function read(Holder $holder): Node {
                    Node $body = $holder->body;
                    return $body;
                }

                function write(): Holder {
                    return new Holder() with [
                        body => new Node(),
                    ];
                }
                """));

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_WithQuotedPropertyKey_ResolvesName()
    {
        // Property names colliding with Tyhp keywords / builtin type names are written quoted.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Node = struct {
                string $type = '';
            };

            function demo(): Node {
                return new Node() with ['type' => 'binary'];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty);
    }

    [Fact]
    public void Check_WithQuotedUnknownPropertyKey_ReportsErrorWithName()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Node = struct {
                string $type = '';
            };

            function demo(): Node {
                return new Node() with ['missing' => 'x'];
            }
            """);

        var error = diagnostics.Errors
            .Should().ContainSingle(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty).Subject;
        error.FormatParams.Should().Contain("missing");
    }

    [Fact]
    public void Check_EvalUsage_ReportsInfo()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                eval('echo 1;');
            }
            """);

        diagnostics.All.Should().Contain(d => d.Code == MessageCode.CheckerEvalUsage);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TraitProvidedProperty_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasName {
                public string $name = '';
            }

            class Person {
                use HasName;
            }

            function demo(): void {
                Person $p = new Person();
                $p->name = 'hello';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TraitUser_StillFlagsMissingProperty()
    {
        // CHECKER_GAPS P0 #8: using any trait must not blanket-suppress TYHP4134.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasName {
                public string $name = '';
            }

            class Person {
                use HasName;
            }

            function demo(): void {
                Person $p = new Person();
                $p->missing = 5;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TransitiveTraitProvidedProperty_DoesNotReport()
    {
        // Trait A uses trait B; B declares the property. Person only writes `use A;`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasNameCore {
                public string $name = '';
            }

            trait HasName {
                use HasNameCore;
            }

            class Person {
                use HasName;
            }

            function demo(): void {
                Person $p = new Person();
                $p->name = 'hello';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_InheritedTraitProvidedProperty_DoesNotReport()
    {
        // The base class uses the trait; the property must be visible through inheritance too.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasName {
                public string $name = '';
            }

            class Base {
                use HasName;
            }

            class Person extends Base {
            }

            function demo(): void {
                Person $p = new Person();
                $p->name = 'hello';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_MissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
                public int $x = 0;
            }

            function demo(): void {
                Plain $p = new Plain();
                $p->missing = 5;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_ExactStdClass_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                \stdClass $o = new \stdClass();
                $o->x = 1;
                new \stdClass()->nope = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_StdClassSubclass_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag extends \stdClass {
            }

            function demo(): void {
                Bag $b = new Bag();
                $b->x = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_AllowDynamicPropertiesStamp_IsNotAnOptIn()
    {
        // Layer 3 omits \AllowDynamicProperties, so the stamp is TYHP4126 (not an
        // attribute class). The named write gate must not treat the leftover name as
        // an opt-in the way the old parent-walk did.
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            #[\AllowDynamicProperties]
            class Opt {
            }

            function demo(): void {
                Opt $o = new Opt();
                $o->x = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_IncompleteClassHarvestStamp_ReportsProhibited()
    {
        var diagnostics = CompileAndCheckFiles(
            ("harvest.tyhpdef", """
                <?tyhpdef
                #[\AllowDynamicProperties]
                final class __PHP_Incomplete_Class {
                }
                """),
            ("test.tyhp", """
                <?tyhp
                function demo(): void {
                    \__PHP_Incomplete_Class $o = new \__PHP_Incomplete_Class();
                    $o->x = 1;
                }
                """));

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_NamespacedStdClassLookalike_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;

            class stdClass {
            }

            function demo(): void {
                \App\stdClass $o = new \App\stdClass();
                $o->x = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UserClassWithoutSet_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
            }

            function demo(): void {
                Plain $p = new Plain();
                $p->nope = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_StdClassUndeclaredRead_StaysUnresolved()
    {
        // Undeclared reads are unchanged: no property and no __get → Unresolved.
        // D.1 does not add a stdClass property map.
        var (diagnostics, checker, file) = CompileAndInspect("""
            <?tyhp
            function demo(): mixed {
                \stdClass $o = new \stdClass();
                $o->x = 1;
                return $o->x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);

        var returned = FindAllAst<PhpJumpStatementAst>(file)
            .Where(j => j.JumpType == PhpJumpType.Return)
            .Select(j => j.Expression)
            .OfType<PhpDereferenceableAst>()
            .SingleOrDefault(d =>
                d.Suffix is PhpInstanceMemberAccessAst member
                && string.Equals(GetMemberName(member.MemberName), "x", StringComparison.Ordinal));

        returned.Should().NotBeNull("expected `return $o->x`");
        checker.ExpressionTypes.TryGetValue(returned!, out var type).Should().BeTrue();
        type.Should().Be(CheckedTypes.Unresolved);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_ObjectCast_DoesNotReport()
    {
        // D.2: (object) infers exact \stdClass, so D.1 allows undeclared writes on that value.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $o = (object)[];
                $o->x = 1;
                ((object)['a' => 1])->nope = 2;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_ObjectCastOfTypedValue_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): void {
                $o = (object)$x;
                $o->y = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_ObjectCastDoesNotLicenseOtherTypes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
            }

            function demo(): void {
                (object)[];
                Plain $p = new Plain();
                $p->nope = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_ObjectCastOfNonStdClassInstance_ReportsProhibited()
    {
        // PHP performs no conversion when the operand of `(object)` is already an object: the
        // result is the same instance, of the same class — `(object)$p` here is still a `Plain`,
        // never a new \stdClass. An explicit `(object)` cast must not be a loophole around the
        // named write gate for a type that isn't \stdClass.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
            }

            function demo(): void {
                Plain $p = new Plain();
                $o = (object)$p;
                $o->nope = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_GenericExtendsStdClass_ReportsProhibited()
    {
        // FOUND_BUGS #38: T extends \stdClass is not the named gate (T includes subclasses).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Bag extends \stdClass {
            }

            function f<T extends \stdClass>(T $x): void {
                $x->y = 1;
            }

            function demo(): void {
                f(new Bag());
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_GenericExtendsClassWithProperty_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Named {
                public string $name = '';
            }

            function f<T extends Named>(T $x): void {
                $x->name = 'ok';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_GenericExtendsClassMissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
            }

            function f<T extends Plain>(T $x): void {
                $x->missing = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_ClassGenericExtendsStdClass_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box<T extends \stdClass> {
                public function put(T $x): void {
                    $x->y = 1;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UnconstrainedGeneric_DoesNotReport()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            function f<T>(T $x): void {
                $x->y = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UnionMissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
            }

            class Bar {
            }

            function demo(Foo|Bar $x): void {
                $x->missing = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UnionSharedProperty_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public int $n = 0;
            }

            class Bar {
                public int $n = 0;
            }

            function demo(Foo|Bar $x): void {
                $x->n = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UnionOneArmMissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public int $n = 0;
            }

            class Bar {
            }

            function demo(Foo|Bar $x): void {
                $x->n = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UnionWithStdClass_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
            }

            function demo(Foo|\stdClass $x): void {
                $x->y = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_IntersectionMissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface A {
            }

            interface B {
            }

            function demo(A&B $x): void {
                $x->missing = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_IntersectionPropertyOnOneArm_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Named {
                public string $name;
            }

            interface Other {
            }

            function demo(Named&Other $x): void {
                $x->name = 'ok';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_NullableObjectMissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
            }

            function demo(?Plain $x): void {
                $x->missing = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_NullableStdClass_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(?\stdClass $x): void {
                $x->y = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_GenericExtendsUnionOneArmMissingProperty_ReportsProhibited()
    {
        // FOUND_BUGS #38: a union bound reports when any object arm would reject the write,
        // even when the union is reached through a generic-parameter constraint.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public int $n = 0;
            }

            class Bar {
            }

            function f<T extends Foo|Bar>(T $x): void {
                $x->n = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_GenericExtendsIntersectionWithNamedArm_DoesNotReport()
    {
        // FOUND_BUGS #38: an intersection bound allows the write when any arm declares the
        // property, even when the intersection is reached through a generic-parameter constraint.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Named {
                public string $name = '';
            }

            interface Other {
            }

            function f<T extends Named&Other>(T $x): void {
                $x->name = 'ok';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_GenericExtendsIntersectionWithStdClassArm_ReportsProhibited()
    {
        // FOUND_BUGS #38: T extends \stdClass&Other reaches the \stdClass arm through a
        // generic-parameter constraint, so the named gate must not fire for it either.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Other {
            }

            function f<T extends \stdClass&Other>(T $x): void {
                $x->y = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TransitiveGenericConstraintToStdClass_ReportsProhibited()
    {
        // FOUND_BUGS #38: T extends U, U extends \stdClass — the named gate must not leak
        // through a second layer of generic-parameter indirection.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f<T extends U, U extends \stdClass>(T $x): void {
                $x->y = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_UnionOfUnconstrainedGenericAndConcreteMissingProperty_ReportsProhibited()
    {
        // A union of an unconstrained (unchecked) generic parameter with a concrete arm that
        // lacks the property must still report: unknown siblings do not mask a checkable
        // arm that would reject the write.
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            class Bar {
            }

            function f<T>(T|Bar $x): void {
                $x->n = 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DuplicateImport_ReportsWarning()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App\Model;

            class User {}

            use App\Model\User;
            use App\Model\User;
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_SingleUnusedImport_ReportsUnusedButNotDuplicate()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;

            use App\Missing\Thing;

            class Demo {}
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_SingleUsedImport_ReportsNeitherUnusedNorDuplicate()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Model {
                class User {}
            }

            namespace App {
                use App\Model\User;

                function demo(): void {
                    User $u = new User();
                }
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInParameterType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(Widget $w): void {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInReturnType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): Widget {
                    return new Widget();
                }
                """));

        // Even if `new Widget()` also marks it, return-only probes should work — use abstract:
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInReturnTypeAnnotation_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): ?Widget {
                    return null;
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInStaticAccess_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {
                    public static function make(): void {}
                }
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): void {
                    Widget::make();
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInGenericTypeArg_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(array<string, Widget> $items): void {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInClosureParameterType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): void {
                    $f = function (Widget $w): void {};
                    $f(new Widget());
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInClosureReturnType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): void {
                    $f = function (): Widget {
                        return new Widget();
                    };
                    $f();
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInCatchClauseType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("MyException.tyhp", """
                <?tyhp
                namespace Lib;
                class MyException extends \Exception {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\MyException;
                function demo(): void {
                    try {
                        echo 1;
                    } catch (MyException $e) {
                        echo 2;
                    }
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInStructPropertyType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                type Holder = struct {
                    Widget $widget;
                };
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInImplements_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                interface Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                class Demo implements Widget {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInExtends_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Base.tyhp", """
                <?tyhp
                namespace Lib;
                class Base {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Base;
                class Demo extends Base {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInInterfaceExtends_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                interface Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                interface Demo extends Widget {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyAsExtendsGenericTypeArgument_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Types.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {}
                class Container<T> {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Box;
                use Lib\Container;
                class Demo extends Container<Box> {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_GroupUseImportUsedOnlyInImplements_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Types.tyhp", """
                <?tyhp
                namespace Lib;
                interface Widget {}
                interface Gadget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\{Widget, Gadget};
                class Demo implements Widget, Gadget {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInEnumImplements_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                interface Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                enum Demo implements Widget {
                    case A;
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_GenuinelyUnusedImport_StillReports4130AlongsideHeritageUse()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Types.tyhp", """
                <?tyhp
                namespace Lib;
                interface Widget {}
                class Unrelated {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                use Lib\Unrelated;
                class Demo implements Widget {}
                """));

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnusedImport).Should().Be(1);
        diagnostics.Warnings.Should().Contain(d =>
            d.Code == MessageCode.CheckerUnusedImport
            && d.Message.Contains("Unrelated", StringComparison.Ordinal));
        diagnostics.Warnings.Should().NotContain(d =>
            d.Code == MessageCode.CheckerUnusedImport
            && d.Message.Contains("Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_ImportUsedOnlyInOperatorOverloadSignature_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Amount.tyhp", """
                <?tyhp
                namespace Lib;
                class Amount {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Amount;
                final class Money {
                    public int $cents = 0;
                    operator convert(self $v): Amount {
                        return new Amount();
                    }
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_RenamedImport_UsedThroughAlias_ReportsNothing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Model {
                class User {}
            }

            namespace App {
                use App\Model\User as Account;

                function demo(): void {
                    Account $u = new Account();
                }
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_ImportedNamespace_UsedAsQualifiedPrefix_ReportsNothing()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Deep.tyhp", """
                <?tyhp
                namespace Lib\Inner;

                class Deep {}
                """),
            ("UsesPrefix.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Inner;

                function make(): void {
                    Inner\Deep $d = new Inner\Deep();
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_FullyQualifiedReference_DoesNotCountAsImportUsage()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;

                class Widget {}
                """),
            ("UsesFqn.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Widget;

                function make(): void {
                    \Lib\Widget $w = new \Lib\Widget();
                }
                """));

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_Imports_AreAttributedPerFile()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;

                class Widget {}
                """),
            ("UsesImport.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Widget;

                function make(): void {
                    Widget $w = new Widget();
                }
                """),
            ("UnusedImport.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Widget;

                class Other {}
                """),
            ("NoImports.tyhp", """
                <?tyhp
                namespace App;

                class Plain {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);

        var unused = diagnostics.Warnings
            .Where(d => d.Code == MessageCode.CheckerUnusedImport)
            .ToList();
        unused.Should().ContainSingle();
        unused[0].FileName.Should().Contain("UnusedImport.tyhp");
        unused[0].Message.Should().Contain("Lib\\Widget");

        diagnostics.Warnings.Should().NotContain(d =>
            d.Code == MessageCode.CheckerUnusedImport
            && d.FileName.Contains("UsesImport.tyhp", StringComparison.Ordinal));
        diagnostics.Warnings.Should().NotContain(d =>
            d.Code == MessageCode.CheckerUnusedImport
            && d.FileName.Contains("NoImports.tyhp", StringComparison.Ordinal));
    }

    private static DiagnosticBag CompileAndCheck(string content) =>
        CompileAndCheck(content, requireNoBindErrors: true);

    private static DiagnosticBag CompileAndCheckAllowBindWarnings(string content) =>
        CompileAndCheck(content, requireNoBindErrors: false);

    private static DiagnosticBag CompileAndCheck(
        string content,
        bool requireNoBindErrors,
        string phpVersion = "8.2",
        bool experimentalReadonlyCloneWith = false)
    {
        return CompileAndCheckFiles(
            requireNoBindErrors,
            phpVersion,
            experimentalReadonlyCloneWith,
            ("test.tyhp", content));
    }

    private static DiagnosticBag CompileAndCheckFiles(params (string FileName, string Content)[] files) =>
        CompileAndCheckFiles(
            requireNoBindErrors: true,
            phpVersion: "8.2",
            experimentalReadonlyCloneWith: false,
            files);

    private static DiagnosticBag CompileAndCheckFiles(
        bool requireNoBindErrors,
        string phpVersion,
        bool experimentalReadonlyCloneWith,
        params (string FileName, string Content)[] files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePaths = new List<string>();
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: phpVersion,
                skipChecking: true,
                configure: o =>
                {
                    o.Checker = new CheckerOptions
                    {
                        PhpVersion = phpVersion,
                        ExperimentalReadonlyCloneWith = experimentalReadonlyCloneWith,
                    };
                });
            var result = compilationService.ParseFiles(filePaths, options);
            if (requireNoBindErrors)
            {
                var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
                bindErrors.Should().BeEmpty(
                    $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");
            }

            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics,
                symbolTree,
                result.GlobalScope!,
                options.Checker);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (DiagnosticBag Diagnostics, TyhpChecker Checker, SrcFileAst File) CompileAndInspect(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.2", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
            bindErrors.Should().BeEmpty(
                $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!, options.Checker);
            checker.Check(result.ParsedFiles!);
            return (result.Diagnostics, checker, result.ParsedFiles![0]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string? GetMemberName(IExpression? memberName) =>
        memberName switch
        {
            PhpNameAst name => name.ValueString,
            TokenValueAst token => token.ValueString,
            IExpression expr => expr.Identifier,
            _ => null,
        };

    private static IEnumerable<T> FindAllAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var found in FindAllAst<T>(child))
            {
                yield return found;
            }
        }
    }

    [Fact]
    public void Check_WithReadonlyInPlace_Reports4141()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(): void {
                Color $c = new Color();
                $c with [alpha => 128];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerWithReadonlyInPlace);
    }

    [Fact]
    public void Check_WithReadonlyClone_Php84_WithoutExperimental_Reports4139()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
    }

    [Fact]
    public void Check_WithReadonlyClone_Php85_Allows()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyInPlace);
    }

    [Fact]
    public void Check_WithReadonlyFinalClass_Php84_Reports4140()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
    }

    [Fact]
    public void Check_WithReadonlyClone_Php84_WithExperimental_AllowsNonFinal()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4", experimentalReadonlyCloneWith: true);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
    }

    [Fact]
    public void Check_WithReadonlyNew_Php84_AllowsNonFinal()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(): Color {
                return new Color() with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyInPlace);
    }

    [Fact]
    public void Check_CloneWith_AfterInferredNewWithAssignment_DoesNotReportUnknown()
    {
        // Regression: `$cfg = new Config() with [...]` must infer `$cfg` as Config on first
        // assignment so `clone $cfg with [...]` does not see `unknown` (TYHP4073).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Config {
                public string $name = "";
                public int $value = 0;
            }

            function demo_with(): void {
                $cfg = new Config() with [name => "test", value => 42];
                $clone = clone $cfg with [name => "updated"];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneNonObject);
    }

    [Fact]
    public void Check_ExistenceGate_UnqualifiedName_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_WrongNamespace_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_EmptyName_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_OtherFunctionName_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('asdfasdfasdfasdf')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_FullyQualifiedName_Accepted()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('\\App\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_NamespaceConcat_Accepted()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists(__NAMESPACE__ . '\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }
}
