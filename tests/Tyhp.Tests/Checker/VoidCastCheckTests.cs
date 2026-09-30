using System;
using System.IO;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Parser;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Cast typing: PHP 8.5 <c>(void)</c> discard, and <c>(object)</c> → <c>\stdClass</c>.
/// </summary>
[Trait("Category", "Checker")]
public class VoidCastCheckTests
{
    [Fact]
    public void VoidCast_Statement_TypeChecksOperandAndInfersVoid()
    {
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            function demo(): void {
                string $s = "hi";
                (void)$s;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));

        var voidCast = FindVoidCasts(file).Should().ContainSingle().Subject;
        checker.ResolveExpressionType(voidCast, new CheckerState()).DisplayName.Should().Be("void");
    }

    [Fact]
    public void VoidCast_OperandWithTypeError_StillReportsOperandDiagnostics()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                (void)\strlen([]);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void VoidCast_ForInitAndUpdate_TypeChecksCleanly()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                for ((void)\strlen("a"); true; (void)\strlen("b")) {
                    break;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void VoidCast_NonFinalForCondition_DoesNotRequireBoolOnVoid()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                for (; (void)\strlen("x"), true; ) {
                    break;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void VoidCast_MixedOperand_DoesNotReportMixedRequiresNarrowing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $m): void {
                (void)$m;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void NoDiscard_DiscardedCall_Reports4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard]
            function important(): int {
                return 1;
            }
            function demo(): void {
                important();
            }
            """);

        var warning = NoDiscardWarnings(diagnostics).Should().ContainSingle().Subject;
        warning.Message.Should().Be(
            "Return value of `\\important` is marked `#[\\NoDiscard]` and must be used or discarded with `(void)`");
    }

    [Fact]
    public void NoDiscard_LiteralMessage_IsIncludedIn4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard('do not ignore')]
            function important(): int {
                return 1;
            }
            function demo(): void {
                important();
            }
            """);

        var warning = NoDiscardWarnings(diagnostics).Should().ContainSingle().Subject;
        warning.Message.Should().Be(
            "Return value of `\\important` is marked `#[\\NoDiscard]` and must be used or discarded with `(void)`: do not ignore");
    }

    [Fact]
    public void NoDiscard_NamedLiteralMessage_IsIncludedIn4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard(message: "call bar instead")]
            function important(): int {
                return 1;
            }
            function demo(): void {
                important();
            }
            """);

        var warning = NoDiscardWarnings(diagnostics).Should().ContainSingle().Subject;
        warning.Message.Should().Contain("call bar instead");
        warning.Message.Should().StartWith(
            "Return value of `\\important` is marked `#[\\NoDiscard]` and must be used or discarded with `(void)`:");
    }

    [Fact]
    public void NoDiscard_InterpolatedMessage_KeepsOneArgText()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard("use $argv instead")]
            function important(): int {
                return 1;
            }
            function demo(): void {
                important();
            }
            """);

        var warning = NoDiscardWarnings(diagnostics).Should().ContainSingle().Subject;
        warning.Message.Should().Be(
            "Return value of `\\important` is marked `#[\\NoDiscard]` and must be used or discarded with `(void)`");
        warning.Message.Should().NotContain("argv");
    }

    [Fact]
    public void NoDiscard_InterfaceCallee_DoesNotWarn_ImplementorWithAttributeDoes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                #[\NoDiscard]
                public function marked(): int;
            }
            class Impl implements I {
                #[\NoDiscard]
                public function marked(): int {
                    return 1;
                }
            }
            function demo(I $iface, Impl $impl): void {
                $iface->marked();
                $impl->marked();
            }
            """);

        var warnings = NoDiscardWarnings(diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("Impl::marked");
        warnings[0].Message.Should().NotContain("I::marked");
    }

    [Fact]
    public void NoDiscard_ImplementorWithoutAttribute_DoesNotWarn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface I {
                #[\NoDiscard]
                public function marked(): int;
            }
            class Impl implements I {
                public function marked(): int {
                    return 1;
                }
            }
            function demo(Impl $impl): void {
                $impl->marked();
            }
            """);

        NoDiscardWarnings(diagnostics).Should().BeEmpty();
    }

    [Fact]
    public void NoDiscard_AbstractCallee_DoesNotWarn_OverrideWithAttributeDoes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class Base {
                #[\NoDiscard]
                abstract public function marked(): int;
            }
            class Concrete extends Base {
                #[\NoDiscard]
                public function marked(): int {
                    return 1;
                }
            }
            function demo(Base $base, Concrete $concrete): void {
                $base->marked();
                $concrete->marked();
            }
            """);

        var warnings = NoDiscardWarnings(diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("Concrete::marked");
        warnings[0].Message.Should().NotContain("Base::marked");
    }

    [Fact]
    public void NoDiscard_OverrideWithoutAttribute_DoesNotWarn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class ParentMarked {
                #[\NoDiscard]
                public function marked(): int {
                    return 1;
                }
            }
            class Child extends ParentMarked {
                public function marked(): int {
                    return 2;
                }
            }
            function demo(Child $child): void {
                $child->marked();
            }
            """);

        NoDiscardWarnings(diagnostics).Should().BeEmpty();
    }

    [Fact]
    public void NoDiscard_TraitImportedMethod_Warns()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T {
                #[\NoDiscard]
                public function marked(): int {
                    return 1;
                }
            }
            class C {
                use T;
            }
            function demo(): void {
                C $c = new C();
                $c->marked();
            }
            """);

        NoDiscardWarnings(diagnostics).Should().ContainSingle();
    }

    [Fact]
    public void NoDiscard_ParentCallInOverride_Warns()
    {
        // `parent::marked()` invokes the parent's own declaration (forwarding `$this`) exactly
        // like `$this->marked()` would if the child did not override — the `::` call syntax must
        // not be mistaken for a static-only call and skip NoDiscard resolution.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class ParentMarked {
                #[\NoDiscard]
                public function marked(): int {
                    return 1;
                }
            }
            class Child extends ParentMarked {
                #[\NoDiscard]
                public function marked(): int {
                    parent::marked();
                    return 2;
                }
            }
            """);

        var warning = NoDiscardWarnings(diagnostics).Should().ContainSingle().Subject;
        warning.Message.Should().Contain("ParentMarked::marked");
    }

    [Fact]
    public void NoDiscard_ImplementorVoidCast_Suppresses4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Impl {
                #[\NoDiscard]
                public function marked(): int {
                    return 1;
                }
            }
            function demo(Impl $impl): void {
                (void)$impl->marked();
                int $n = $impl->marked();
            }
            """);

        NoDiscardWarnings(diagnostics).Should().BeEmpty();
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void NoDiscard_VoidCast_Suppresses4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard]
            function important(): int {
                return 1;
            }
            function demo(): void {
                (void)important();
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void NoDiscard_UsedReturn_DoesNotWarn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard]
            function important(): int {
                return 1;
            }
            function demo(): void {
                int $n = important();
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void OrdinaryDiscardedCall_WithoutNoDiscard_DoesNotWarn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function sideEffect(): int {
                return 1;
            }
            function demo(): void {
                sideEffect();
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void ObjectCast_EmptyArray_HasStdClassType()
    {
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            function demo(): void {
                (object)[];
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));

        var objectCast = FindObjectCasts(file).Should().ContainSingle().Subject;
        var type = checker.ResolveExpressionType(objectCast, new CheckerState());
        var simple = type.Should().BeOfType<SimpleCheckedType>().Subject;
        simple.ResolvedSymbol.Should().BeOfType<ObjectDeclarationSymbol>();
        simple.ResolvedSymbol.Should().NotBeOfType<BuiltInTypeSymbol>();
        simple.DisplayName.Should().Be(@"\stdClass");
    }

    [Fact]
    public void ObjectCast_Array_IsAssignableToStdClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                \stdClass $o = (object)['a' => 1];
                \stdClass $empty = (object)[];
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void ObjectCast_IsAssignableToBuiltinObject()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                object $o = (object)[];
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void BuiltinObject_IsNotAssignableToStdClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(object $o): void {
                \stdClass $s = $o;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void ObjectCast_OnBuiltinObject_IsRedundant()
    {
        // PHP performs no conversion when the operand is already an object (same instance,
        // same class): `(object)$x` for `object $x` is a no-op, not a narrowing to \stdClass.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(object $x): void {
                (object)$x;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerRedundantCast);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void ObjectCast_OnStdClass_IsRedundant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(\stdClass $x): void {
                (object)$x;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerRedundantCast);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void ObjectCast_OnUserClassInstance_IsRedundant()
    {
        // Casting an already-object value never re-wraps it in \stdClass, regardless of the
        // concrete class — `(object)$plain` keeps `$plain`'s own type (`Plain`), so the cast is
        // an identity no-op and must not be typed \stdClass.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
            }
            function demo(Plain $x): void {
                (object)$x;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerRedundantCast);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void ObjectCast_OnArray_IsNotRedundant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array $x): void {
                (object)$x;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerRedundantCast);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void ObjectCast_OnUserClassInstance_KeepsOwnTypeNotStdClass()
    {
        // `(object)` never re-wraps an already-object value in a new \stdClass instance, so the
        // static type of `(object)$plain` must stay `Plain`, not `\stdClass`.
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            class Plain {
            }
            function demo(Plain $x): void {
                (object)$x;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));

        var objectCast = FindObjectCasts(file).Should().ContainSingle().Subject;
        var type = checker.ResolveExpressionType(objectCast, new CheckerState());
        type.DisplayName.Should().Be(@"\Plain");
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ",
            diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")
                .Concat(diagnostics.Warnings.Select(w => $"W{w.Code}: {w.Message}")));

    private static List<IDiagnostic> NoDiscardWarnings(DiagnosticBag diagnostics)
        => diagnostics.Warnings.Where(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused).ToList();

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var (_, _, diagnostics) = Compile(content);
        return diagnostics;
    }

    private static (TyhpChecker checker, SrcFileAst file, DiagnosticBag diagnostics) Compile(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.5", skipChecking: true);

            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var file = result.ParsedFiles![0];
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (checker, file, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static List<PhpUnaryOpAst> FindVoidCasts(IBase2Ast root) =>
        FindCasts(root, TyhpParser.T_VOID_CAST);

    private static List<PhpUnaryOpAst> FindObjectCasts(IBase2Ast root) =>
        FindCasts(root, TyhpParser.T_OBJECT_CAST);

    private static List<PhpUnaryOpAst> FindCasts(IBase2Ast root, int tokenType)
    {
        var found = new List<PhpUnaryOpAst>();
        Collect(root, found);
        return found;

        void Collect(IBase2Ast? node, List<PhpUnaryOpAst> dest)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpUnaryOpAst unary
                && unary.Operator?.ValueInt64 is long token
                && (int)token == tokenType)
            {
                dest.Add(unary);
            }

            foreach (var child in node.AstChildren)
            {
                Collect(child, dest);
            }
        }
    }
}
