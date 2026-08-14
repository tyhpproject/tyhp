using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regression tests for call-site argument validation (Story 08 / Story 14 Phase 3 §1).
/// Previously <c>CheckCall</c> never obtained callee parameters, so TYHP4010 and named-argument
/// diagnostics 4079–4081 / 4096 were unreachable.
/// </summary>
[Trait("Category", "Checker")]
public class CallArgumentValidationTests
{
    [Fact]
    public void FreeFunction_WrongArgumentType_ReportsIncompatibleArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function takesInt(int $count): void {}
            function demo(): void {
                takesInt('nope');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void InstanceMethod_WrongArgumentType_ReportsIncompatibleArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function g(int $count): void {}
            }
            function demo(): void {
                C $c = new C();
                $c->g('nope');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ThisMethod_WrongArgumentType_ReportsIncompatibleArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function g(int $count): void {}
                public function call(): void {
                    $this->g('nope');
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void NestedCallArgument_WrongType_ReportsIncompatibleArgumentType()
    {
        // SuppressChildTraversal on PhpDereferenceableAst means nested calls are only checked
        // when ValidateArgumentTypes resolves argument expressions.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function takesInt(int $count): void {}
            function identity(string $s): string { return $s; }
            function demo(): void {
                takesInt(identity('nope'));
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void TrueNestedCallArgument_WrongType_ReportsOnInnerCall()
    {
        // Regression: `PhpDereferenceableAst` suppresses the generic checker child-walk
        // (SuppressChildTraversal), so a nested call used purely as an argument — where the
        // *outer* call's own parameter is untyped/`mixed` and therefore reports nothing itself —
        // was never independently visited. `identity(42)` must still be checked (string expected,
        // int given) even though `useValue`'s `mixed` parameter accepts anything.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function identity(string $s): string { return $s; }
            function useValue(mixed $v): void {}
            function demo(): void {
                useValue(identity(42));
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ChainedCall_InnerCallOwnWrongArgumentType_ReportsIncompatibleArgumentType()
    {
        // Regression: for `$a->b('nope')->toStr()`, the outer `.toStr()` call only resolves the
        // *type* of `chain.Base` (`$a->b('nope')`) to find its receiver — it never re-entered the
        // checker for that nested call, so `.b()`'s own bad argument went unreported.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class B {
                public function toStr(): string { return ""; }
            }
            class A {
                public function b(int $count): B { return new B(); }
            }
            function demo(A $a): void {
                $a->b('nope')->toStr();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ChainedCall_InnerCallPrivateVisibility_ReportsMemberNotAccessible()
    {
        // Regression: same receiver-chain gap as above, but for the private-method visibility
        // check — `$a->b()->toStr()` never re-validated the private `.b()` call in the chain.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class B {
                public function toStr(): string { return ""; }
            }
            class A {
                private function b(): B { return new B(); }
            }
            function demo(A $a): void {
                $a->b()->toStr();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void NestedNewArgument_ConstructorWrongArgumentType_ReportsOnInnerConstructor()
    {
        // Regression: same argument-position gap as TrueNestedCallArgument, but for `new` used as
        // an argument rather than a call.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function __construct(int $count) {}
            }
            function useValue(mixed $v): void {}
            function demo(): void {
                useValue(new Box('nope'));
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void StaticMethod_WrongArgumentType_ReportsIncompatibleArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public static function g(int $count): void {}
            }
            function demo(): void {
                C::g('nope');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Constructor_WrongArgumentType_ReportsIncompatibleArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function __construct(int $count) {}
            }
            function demo(): void {
                new C('nope');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void PrivateMethod_CalledFromOutside_ReportsMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                private function g(int $count): void {}
            }
            function demo(): void {
                C $c = new C();
                $c->g(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void DuplicateNamedArgument_ReportsDuplicateNamedArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function foo(int $x, int $y = 0): void {}
            function demo(): void {
                foo(x: 1, x: 2);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDuplicateNamedArgument);
    }

    [Fact]
    public void PositionalAfterNamed_ReportsPositionalAfterNamed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function foo(int $x, int $y): void {}
            function demo(): void {
                foo(x: 1, 2);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPositionalAfterNamed);
    }

    [Fact]
    public void UnknownNamedArgument_ReportsUnknownNamedArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function foo(int $count): void {}
            function demo(): void {
                foo(nonExistent: 1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnknownNamedArgument);
    }

    [Fact]
    public void ValidNamedArgument_NoUnknownNamedArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function foo(int $count): void {}
            function demo(): void {
                foo(count: 1);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnknownNamedArgument);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void SpreadNonIterable_ReportsSpreadNonIterable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function foo(int $a, int $b): void {}
            function demo(): void {
                int $n = 1;
                foo(...$n);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerSpreadNonIterable);
    }

    [Fact]
    public void OverloadedBuiltin_ExtraArgs_UsesMatchingArityOverload()
    {
        // ExtStandard call_user_func uses Rest unpack; extra args must not report TYHP4143.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(callable<int, string> $cb): string {
                return \call_user_func($cb, 1);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void InheritedMethod_WrongArgumentType_ReportsIncompatibleArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function g(int $count): void {}
            }
            class Child extends Base {}
            function demo(): void {
                Child $c = new Child();
                $c->g('nope');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void MissingRequiredArgument_Reports4142()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                private function initFiber(callable $executor): void {}
                public function __construct(): void {
                    $this->initFiber();
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void OptionalArgumentOmitted_No4142()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                private function init(int $n = 0): void {}
                public function __construct(): void {
                    $this->init();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void TooManyArguments_Reports4143()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function takesOne(int $n): void {}
            function demo(): void {
                takesOne(1, 2);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTooManyArguments);
    }

    [Fact]
    public void VariadicAllowsExtraArguments_No4143()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function takesMany(int ...$ns): void {}
            function demo(): void {
                takesMany(1, 2, 3);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void VariadicExtraArguments_AreCheckedAgainstElementType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function takesMany(int ...$ns): void {}
            function demo(): void {
                takesMany(1, 2, 'nope');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ConstructorMissingRequiredArgument_Reports4142()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function __construct(int $n): void {}
            }
            function demo(): void {
                new Box();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void ArrayCallableLiteral_ObjectAndMethodString_AssignableToCallableParam()
    {
        // Idiomatic PHP array-callable form `[$obj, 'method']` — a two-element positional array
        // literal — must satisfy a `callable`-typed parameter, matching real-world interop code
        // (framework dispatchers, \Tyhp\Generic::bindCallable's own [$obj, 'method'] handling).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function greet(): string { return "hi"; }
            }
            function invoke(callable $cb): mixed {
                return $cb();
            }
            function demo(): void {
                C $c = new C();
                invoke([$c, 'greet']);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ArrayCallableLiteral_UnknownObjectReceiver_AssignableToCallableParam()
    {
        // Mirrors runtime/packages/core/tyhp_src/Generic.tyhp `newWithArgs`:
        // `\call_user_func_array_unsafe([$this->target, '__construct'], $constructorArguments)`,
        // where the receiver is typed as plain `object` (its concrete class is unknown statically,
        // which is exactly why the call is runtime-guarded with `\method_exists` first).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(object $target, array $args): void {
                \call_user_func_array_unsafe([$target, '__construct'], $args);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ArrayCallableLiteral_StaticClassNameForm_AssignableToCallableParam()
    {
        // The other idiomatic array-callable shape: `['ClassName', 'method']` for a static method.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke(callable $cb): mixed {
                return $cb();
            }
            function demo(string $className, string $methodName): void {
                invoke([$className, $methodName]);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void ArrayCallableLiteral_DeclaredCallableVariable_NoTypeMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function greet(): string { return "hi"; }
            }
            function demo(): void {
                C $c = new C();
                callable $cb = [$c, 'greet'];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void NonCallableShapedArrayLiteral_StillRejectedAsCallable()
    {
        // Regression guard: the array-callable carve-out must stay scoped to the exact two-element
        // `[receiver, methodName]` shape — an arbitrary array literal (wrong arity/element types)
        // must still be rejected the same way a bare `array` value is (CHECKER_GAPS P1 review).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke(callable $cb): mixed {
                return $cb();
            }
            function demo(): void {
                invoke([1, 2, 3]);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void PlainArrayVariable_StillRejectedAsCallable()
    {
        // Regression guard: only the literal shape is special-cased — a plain `array`-typed
        // variable (no positional-literal shape visible at the call site) must remain rejected.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke(callable $cb): mixed {
                return $cb();
            }
            function demo(array $arr): void {
                invoke($arr);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
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
                PhpVersion = "8.2",
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
