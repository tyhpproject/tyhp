using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 4 — infer <c>\Closure&lt;TCallableShape, TThis, TScope&gt;</c>
/// from literals, first-class callables, and <c>fromCallable</c>.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.6")]
public class ClosureInferenceTests
{
    [Fact]
    public void FnLiteral_InfersCallableShapeAndEnclosingThis()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = fn (int $x): string => "a";
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirst<PhpInlineFunctionAst>(checker, file);
        type.DisplayName.Should().Contain("callable", type.DisplayName);
        type.DisplayName.Should().Contain("Host", type.DisplayName);
        IsClosureGeneric(type).Should().BeTrue(type.DisplayName);
        ThisSlot(type).Should().Contain("Host");
        ScopeSlot(type).Should().Contain("Host");
        ShapeSlot(type).Should().Contain("int", type.DisplayName);
        ShapeSlot(type).Should().Contain("string", type.DisplayName);
    }

    [Fact]
    public void FunctionLiteral_InfersCallableShapeAndEnclosingThis()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (int $x): string {
                        return "a";
                    };
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirst<PhpInlineFunctionAst>(checker, file);
        ThisSlot(type).Should().Contain("Host");
        ScopeSlot(type).Should().Contain("Host");
    }

    [Fact]
    public void StaticFn_TThisIsNull()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = static function (): void {};
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirst<PhpInlineFunctionAst>(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("Host");
    }

    [Fact]
    public void StaticArrowFn_TThisIsNull()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = static fn (int $x): string => "a";
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirst<PhpInlineFunctionAst>(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("Host");
        ShapeSlot(type).Should().Contain("int", type.DisplayName);
        ShapeSlot(type).Should().Contain("string", type.DisplayName);
    }

    [Fact]
    public void AnnotatedClosure_InvokeFacet_TakesIntReturnsString()
    {
        var errors = Compile("""
            <?tyhp
            function take(callable(int): string $c): void {}

            function demo(\Closure<callable(int): string> $c): string {
                take($c);
                return $c(1);
            }
            """).Errors;

        errors.Should().BeEmpty(
            "\\Closure<callable(int): string> must invoke as takes int, returns string: "
            + Describe(errors));
    }

    [Fact]
    public void AnnotatedClosure_InvokeWrongArgType_ReportsMismatch()
    {
        var errors = Compile("""
            <?tyhp
            function demo(\Closure<callable(int): string> $c): string {
                return $c("nope");
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType
                || e.Code == MessageCode.CheckerTypeMismatch,
            "string argument against Closure<callable(int): string> must fail: "
            + Describe(errors));
    }

    [Fact]
    public void AnnotatedClosure_InvokeReturnIsString_NotInt()
    {
        var errors = Compile("""
            <?tyhp
            function demo(\Closure<callable(int): string> $c): void {
                int $n = $c(1);
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "Closure<callable(int): string> invoke return is string, not int: "
            + Describe(errors));
    }

    [Fact]
    public void UserInvokableClass_StillGetsCallableFacets()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(int $x): string {
                    return "a";
                }
            }

            function take(callable(int): string $c): void {}

            function demo(Handler $h): string {
                take($h);
                return $h(1);
            }
            """).Errors;

        errors.Should().BeEmpty(
            "non-Closure __invoke classes must still get callable facets: "
            + Describe(errors));
    }

    [Fact]
    public void UserInvokableClass_MismatchedArity_DoesNotAssignToTypedCallable()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(): int {
                    return 1;
                }
            }

            function take(callable(string): bool $c): void {}

            function demo(Handler $h): void {
                take($h);
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch
                || e.Code == MessageCode.CheckerIncompatibleArgumentType,
            "Handler with __invoke(): int must not assign to callable(string): bool: "
            + Describe(errors));
    }

    [Fact]
    public void UserInvokableClass_MismatchedReturn_DoesNotAssignToTypedCallable()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(): int {
                    return 1;
                }
            }

            function take(callable(): bool $c): void {}

            function demo(Handler $h): void {
                take($h);
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch
                || e.Code == MessageCode.CheckerIncompatibleArgumentType,
            "Handler with __invoke(): int must not assign to callable(): bool: "
            + Describe(errors));
    }

    [Fact]
    public void UserInvokableClass_UntypedCallable_StillAcceptsAnyInvoke()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(): int {
                    return 1;
                }
            }

            function take(callable $c): void {}

            function demo(Handler $h): void {
                take($h);
            }
            """).Errors;

        errors.Should().BeEmpty(
            "untyped callable still accepts any public __invoke object: "
            + Describe(errors));
    }

    [Fact]
    public void UserInvokableClass_OptionalInvokeParameter_AcceptsBothArities()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(string $a, int $b = 0): void {}
            }

            function demo(Handler $h): void {
                $h("x");
                $h("x", 1);
            }
            """).Errors;

        errors.Should().BeEmpty(
            "$h(...) on a __invoke(string, int = 0) class must accept both the required-only "
            + "and full-arity call: " + Describe(errors));
    }

    [Fact]
    public void UserInvokableClass_WrongArgType_ReportsMismatch()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(int $x): string {
                    return "a";
                }
            }

            function demo(Handler $h): string {
                return $h("nope");
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType
                || e.Code == MessageCode.CheckerTypeMismatch,
            "user __invoke(int): string must still reject a string argument: "
            + Describe(errors));
    }

    [Fact]
    public void Annotation_OneArgClosure_AcceptsInferredInstanceFn()
    {
        var errors = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    \Closure<callable(int): string> $fn = fn (int $x): string => "a";
                }
            }
            """).Errors;

        errors.Should().BeEmpty(
            "\\Closure<callable(int): string> must accept an inferred instance closure: "
            + Describe(errors));
    }

    [Fact]
    public void BareClosure_StaysGradual()
    {
        var errors = Compile("""
            <?tyhp
            function demo(\Closure $c): void {
                $c();
            }

            function main(): void {
                demo(function (): void {});
            }
            """).Errors;

        errors.Should().BeEmpty(
            "bare \\Closure must stay gradual and accept a literal: "
            + Describe(errors));
    }

    [Fact]
    public void Fcc_InstanceMethod_TThisIsReceiverNotCaller()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Foo {
                public function m(int $x): void {}
            }

            class Caller {
                public function demo(Foo $obj): void {
                    $c = $obj->m(...);
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirstFcc(checker, file);
        ThisSlot(type).Should().Contain("Foo", type.DisplayName);
        ThisSlot(type).Should().NotContain("Caller", type.DisplayName);
        ScopeSlot(type).Should().Contain("Foo", type.DisplayName);
    }

    [Fact]
    public void Fcc_StaticMethod_TThisNullTScopeIsClass()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Foo {
                public static function m(int $x): void {}
            }

            function demo(): void {
                $c = Foo::m(...);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirstFcc(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("Foo", type.DisplayName);
    }

    [Fact]
    public void Fcc_StaticMethod_Inherited_TScopeIsDeclaringClassNotReferencedClass()
    {
        // PHP: ReflectionFunction::getClosureScopeClass() for `Sub::inherited(...)` (a static
        // method only declared on `Base`) is `Base`, not `Sub` — verified with `php -r` against
        // PHP 8.5. The referenced class name at the call site is not the stored scope when the
        // method is physically declared higher up the hierarchy.
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Base {
                public static function inherited(int $x): void {}
            }

            class Sub extends Base {
            }

            function demo(): void {
                $c = Sub::inherited(...);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirstFcc(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("Base", type.DisplayName);
        ScopeSlot(type).Should().NotContain("Sub", type.DisplayName);
    }

    [Fact]
    public void FromCallable_ArrayStaticInherited_TScopeIsDeclaringClass()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Base {
                public static function inherited(int $x): void {}
            }

            class Sub extends Base {
            }

            function demo(): void {
                $c = \Closure::fromCallable(['Sub', 'inherited']);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("Base", type.DisplayName);
        ScopeSlot(type).Should().NotContain("Sub", type.DisplayName);
    }

    [Fact]
    public void FromCallable_StringStaticInherited_TScopeIsDeclaringClass()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Base {
                public static function inherited(int $x): void {}
            }

            class Sub extends Base {
            }

            function demo(): void {
                $c = \Closure::fromCallable('Sub::inherited');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("Base", type.DisplayName);
        ScopeSlot(type).Should().NotContain("Sub", type.DisplayName);
    }

    [Fact]
    public void Fcc_Function_TThisAndTScopeNull()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            function foo(int $x): string {
                return "a";
            }

            function demo(): void {
                $c = foo(...);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirstFcc(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Be("null", type.DisplayName);
    }

    [Fact]
    public void FromCallable_ArrayInstance_TThisIsReceiverNotCaller()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Foo {
                public function m(int $x): void {}
            }

            class Caller {
                public function demo(Foo $obj): void {
                    $c = \Closure::fromCallable([$obj, 'm']);
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Contain("Foo", type.DisplayName);
        ThisSlot(type).Should().NotContain("Caller", type.DisplayName);
        ScopeSlot(type).Should().Contain("Foo", type.DisplayName);
    }

    [Fact]
    public void FromCallable_Strlen_TThisAndTScopeNull()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            function demo(): void {
                $c = \Closure::fromCallable('strlen');
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Be("null", type.DisplayName);
        type.DisplayName.Should().Contain("callable", type.DisplayName);
    }

    [Fact]
    public void FromCallable_StaticMethodArray_TThisNullTScopeIsClass()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class A {
                public static function staticMethod(int $x): void {}
            }

            function demo(): void {
                $c = \Closure::fromCallable([A::class, 'staticMethod']);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Be("null", type.DisplayName);
        ScopeSlot(type).Should().Contain("A", type.DisplayName);
    }

    [Fact]
    public void FromCallable_PrivateMethodFromOtherClass_Reports4025()
    {
        var errors = Compile("""
            <?tyhp
            class A {
                private function secret(): void {}
            }

            class B {
                public function demo(A $a): void {
                    \Closure::fromCallable([$a, 'secret']);
                }
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerMemberNotAccessible,
            "fromCallable of another class's private method must error: "
            + Describe(errors));
    }

    [Fact]
    public void FromCallable_PrivateMethodFromSameClass_Allowed()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class A {
                private function secret(): void {}

                public function demo(): void {
                    $c = \Closure::fromCallable([$this, 'secret']);
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerMemberNotAccessible,
            "fromCallable of a private method from the declaring class must be allowed: "
            + Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Contain("A", type.DisplayName);
    }

    [Fact]
    public void FromCallable_Invokable_StoresArityIntersectionNotUnion()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(string $a, int $b = 0): void {}
            }

            function demo(Handler $h): void {
                $c = \Closure::fromCallable($h);
                $c("x");
                $c("x", 1);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        var shape = ShapeSlot(type);
        shape.Should().Contain("&", "TCallableShape must be an intersection of arities, not a union: " + type.DisplayName);
        shape.Should().NotContain("|", type.DisplayName);
        shape.Should().NotContain("Handler", "class type must be dropped from TCallableShape: " + type.DisplayName);
        ThisSlot(type).Should().Contain("Handler", type.DisplayName);
        ScopeSlot(type).Should().Contain("Handler", type.DisplayName);
    }

    [Fact]
    public void FromCallable_Invokable_DoesNotStayAsTheClass()
    {
        var errors = Compile("""
            <?tyhp
            class Handler {
                public function __invoke(string $a): void {}
            }

            function demo(Handler $h): void {
                Handler $wrong = \Closure::fromCallable($h);
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch
                || e.Code == MessageCode.CheckerIncompatibleReturnType,
            "fromCallable of an invokable must not type as the class: "
            + Describe(errors));
    }

    [Fact]
    public void CurrentScope_NotCopiedOntoFromCallableTThis()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Foo {
                public function m(): void {}
            }

            class Caller {
                public function demo(Foo $f): void {
                    $c = \Closure::fromCallable([$f, 'm']);
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFromCallable(checker, file);
        ThisSlot(type).Should().Contain("Foo", type.DisplayName);
        ThisSlot(type).Should().NotContain("Caller", type.DisplayName);
    }

    private static bool IsClosureGeneric(ICheckedType type) =>
        type is GenericCheckedType generic
        && generic.TypeArguments.Count >= 1
        && generic.BaseType.DisplayName.Contains("Closure", StringComparison.OrdinalIgnoreCase);

    private static string ShapeSlot(ICheckedType type) =>
        type is GenericCheckedType { TypeArguments.Count: > 0 } g
            ? g.TypeArguments[0].DisplayName
            : type.DisplayName;

    private static string ThisSlot(ICheckedType type) =>
        type is GenericCheckedType { TypeArguments.Count: > 1 } g
            ? g.TypeArguments[1].DisplayName
            : "";

    private static string ScopeSlot(ICheckedType type) =>
        type is GenericCheckedType { TypeArguments.Count: > 2 } g
            ? g.TypeArguments[2].DisplayName
            : "";

    private static ICheckedType InferredTypeOfFirst<T>(TyhpChecker checker, SrcFileAst file)
        where T : class, IBase2Ast
    {
        var node = FindAllAst<T>(file).First();
        checker.ExpressionTypes.TryGetValue(node, out var type).Should().BeTrue(
            "checker should have memoized the producer expression type");
        type.Should().NotBeNull();
        return type!;
    }

    private static ICheckedType InferredTypeOfFirstFcc(TyhpChecker checker, SrcFileAst file)
    {
        var call = FindAllAst<PhpCallAst>(file).First(c =>
            CheckerHelpers.IsFirstClassCallableArgumentList(c.Arguments));
        var deref = FindAllAst<PhpDereferenceableAst>(file)
            .First(d => ReferenceEquals(d.Suffix, call));
        if (checker.ExpressionTypes.TryGetValue(deref, out var type) && type is not null)
        {
            return type;
        }

        if (checker.ExpressionTypes.TryGetValue(call, out type) && type is not null)
        {
            return type;
        }

        throw new InvalidOperationException(
            "No inferred type for FCC. Keys: "
            + string.Join(", ", checker.ExpressionTypes.Keys.Select(k => k.GetType().Name)));
    }

    private static ICheckedType InferredTypeOfFromCallable(TyhpChecker checker, SrcFileAst file)
    {
        foreach (var deref in FindAllAst<PhpDereferenceableAst>(file))
        {
            if (deref.Suffix is not PhpCallAst)
            {
                continue;
            }

            if (!checker.ExpressionTypes.TryGetValue(deref, out var type) || type is null)
            {
                continue;
            }

            if (IsClosureGeneric(type) && type.DisplayName.Contains("Closure", StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        throw new InvalidOperationException(
            "No inferred Closure type for fromCallable. Seen: "
            + string.Join("; ", checker.ExpressionTypes.Values.Select(v => v?.DisplayName)));
    }

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

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static (TyhpChecker Checker, SrcFileAst File, IReadOnlyList<IDiagnostic> Errors) Compile(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (checker, result.ParsedFiles![0], result.Diagnostics.Errors);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
