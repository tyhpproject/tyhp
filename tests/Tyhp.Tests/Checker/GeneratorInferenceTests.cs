using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 6b — infer <c>\Generator&lt;TKey, TValue, TSend, TReturn&gt;</c>
/// from generator bodies. Declared return is the Generator the caller receives,
/// never the body's <c>return</c> payload.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.6")]
public class GeneratorInferenceTests
{
    [Fact]
    public void BareGenerator_YieldInt_CallersSeeInferredFourArg()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                yield 1;
            }

            function consume(): void {
                \Generator<int, int, mixed, null> $g = foo();
            }
            """);

        errors.Should().BeEmpty(
            "bare \\Generator { yield 1; } must infer \\Generator<int, int, mixed, null>: "
            + Describe(errors));
    }

    [Fact]
    public void StringReturnWithYield_Reports4087()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): string {
                yield 1;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerGeneratorInvalidReturnType,
            "function foo(): string { yield 1; } must report TYHP4087: " + Describe(errors));
    }

    [Fact]
    public void TwoArgGenerator_MatchingTValue_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string> {
                yield "a";
            }
            """);

        errors.Should().BeEmpty(
            "yield \"a\" must match Generator<int, string> TValue: " + Describe(errors));
    }

    [Fact]
    public void TwoArgGenerator_MismatchedTValue_Reports4087()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string> {
                yield 1;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerGeneratorInvalidReturnType,
            "yield 1 against Generator<int, string> TValue must report TYHP4087: "
            + Describe(errors));
    }

    [Fact]
    public void TwoArgGenerator_InfersTSendAndTReturnForCallers()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string> {
                int $x = yield "a";
                return true;
            }

            function consume(): void {
                \Generator<int, string, int, bool> $g = foo();
            }
            """);

        errors.Should().BeEmpty(
            "two-arg Generator must pin K/V and infer TSend/TReturn for callers: "
            + Describe(errors));
    }

    [Fact]
    public void TypedYieldTarget_ConstrainsTSendAtCallSite()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                int $x = yield 1;
            }

            function consume(): void {
                \Generator<int, int, int, null> $g = foo();
                $g->send(1);
            }
            """);

        errors.Should().BeEmpty(
            "int $x = yield must make send(int) legal at call sites: " + Describe(errors));
    }

    [Fact]
    public void TypedYieldTarget_RejectsWrongSendAtCallSite()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                int $x = yield 1;
            }

            function consume(): void {
                \Generator<int, int, int, null> $g = foo();
                $g->send("nope");
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType
                || e.Code == MessageCode.CheckerTypeMismatch,
            "send(\"nope\") against inferred TSend=int must fail: " + Describe(errors));
    }

    [Fact]
    public void UntypedYieldAssignment_TSendStaysMixed()
    {
        var (checker, file, errors) = CompileChecked("""
            <?tyhp
            function foo(): \Generator {
                yield 1;
            }
            """);

        errors.Should().BeEmpty("unused yield TSend is mixed: " + Describe(errors));

        var yieldNode = FindAllAst<PhpYieldAst>(file).First();
        InferYieldType(checker, file, yieldNode).DisplayName.Should().Be(
            "mixed",
            "untyped / unused yield must leave TSend mixed");
    }

    [Fact]
    public void UntypedYieldAssignment_TargetIsMixed_NarrowingRequired()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                $x = yield 1;
                string $s = $x;
            }

            function consume(): void {
                \Generator<int, int, mixed, null> $g = foo();
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch
                || e.Code == MessageCode.CheckerMixedRequiresNarrowing,
            "untyped $x = yield 1 must leave $x mixed so assigning to string requires narrowing: "
            + Describe(errors));
    }

    [Fact]
    public void UntypedYieldAssignment_NarrowedWithIs_IsUsable()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                $x = yield 1;
                if ($x is int) {
                    int $n = $x;
                }
            }
            """);

        errors.Should().BeEmpty(
            "untyped $x = yield 1 must be usable after is-narrowing: " + Describe(errors));
    }

    [Fact]
    public void ConflictingTypedYieldTargets_Reports4328()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                int $a = yield 1;
                string $b = yield 2;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerGeneratorSendIntersectionEmpty,
            "int vs string TSend targets must report TYHP4328: " + Describe(errors));
    }

    [Fact]
    public void ReturnInGenerator_ContributesTReturn()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                yield 1;
                return "done";
            }

            function consume(): void {
                \Generator<int, int, mixed, string> $g = foo();
            }
            """);

        errors.Should().BeEmpty(
            "return \"done\" must infer TReturn=string when every path returns: "
            + Describe(errors));
    }

    [Fact]
    public void ReturnOnOnePath_TReturnIncludesNull()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(bool $done): \Generator {
                yield 1;
                if ($done) {
                    return "done";
                }
            }

            function consume(): void {
                \Generator<int, int, mixed, ?string> $g = foo(true);
            }
            """);

        errors.Should().BeEmpty(
            "return on one path plus fall-off must infer TReturn string|null: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_MergesInnerKeyValue_ExpressionIsTReturn()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function inner(): \Generator<int, string, mixed, bool> {
                yield "a";
                return true;
            }

            function outer(): \Generator {
                bool $done = yield from inner();
            }

            function consume(): void {
                \Generator<int, string, mixed, null> $g = outer();
            }
            """);

        errors.Should().BeEmpty(
            "yield from Generator<int, string, mixed, bool> must merge K/V and type as bool: "
            + Describe(errors));
    }

    [Fact]
    public void MethodGenerator_UsesSameInference()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class C {
                public function foo(): \Generator {
                    yield 1;
                }
            }

            function consume(C $c): void {
                \Generator<int, int, mixed, null> $g = $c->foo();
            }
            """);

        errors.Should().BeEmpty(
            "generator methods must infer the same four-arg Generator: " + Describe(errors));
    }

    [Fact]
    public void ClosureGenerator_UsesSameInference()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function consume(): void {
                $gen = function(): \Generator {
                    yield 1;
                };
                \Generator<int, int, mixed, null> $g = $gen();
            }
            """);

        errors.Should().BeEmpty(
            "generator closures must infer the same four-arg Generator: " + Describe(errors));
    }

    [Fact]
    public void FourArgGenerator_BodyMustMatchPins()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string, mixed, int> {
                yield "ok";
                return 0;
            }
            """);

        errors.Should().BeEmpty(
            "four-arg body that matches pins must typecheck: " + Describe(errors));
    }

    [Fact]
    public void FourArgGenerator_MismatchedTValue_Reports4087()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string, mixed, int> {
                yield 1;
                return 0;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerGeneratorInvalidReturnType,
            "yield 1 against four-arg pinned TValue=string must report TYHP4087: "
            + Describe(errors));
    }

    [Fact]
    public void FourArgGenerator_MismatchedTReturn_Reports4087()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string, mixed, int> {
                yield "a";
                return "nope";
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerGeneratorInvalidReturnType,
            "return \"nope\" against four-arg pinned TReturn=int must report TYHP4087: "
            + Describe(errors));
    }

    [Fact]
    public void FourArgGenerator_MismatchedTSend_RejectedAtYieldSite()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator<int, string, string, int> {
                int $x = yield "a";
                return 0;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "int $x = yield against four-arg pinned TSend=string must fail: " + Describe(errors));
    }

    [Fact]
    public void FourArgGenerator_YieldFromIncompatibleInnerTSend_Reports4087()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function inner(): \Generator<int, string, int, mixed> {
                int $y = yield "a";
            }

            function outer(): \Generator<int, string, string, mixed> {
                mixed $r = yield from inner();
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerGeneratorInvalidReturnType,
            "outer pinned TSend=string cannot legally send() into inner's TSend=int: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_Array_MergesKeyValue_TReturnIsNull()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                $r = yield from [1, 2, 3];
            }

            function consume(): void {
                \Generator<int|string, int, mixed, null> $g = foo();
            }
            """);

        errors.Should().BeEmpty(
            "yield from a plain array must merge its key/value types and fall off with "
            + "TReturn=null: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_Array_ExpressionEvaluatesToNull()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): \Generator {
                $r = yield from [1, 2, 3];
                string $s = $r;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "yield from a plain array evaluates to null (no TReturn), not its value type: "
            + Describe(errors));
    }

    [Fact]
    public void CallArgumentYield_ConstrainsTSend()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function takeInt(int $x): void {}

            function foo(): \Generator {
                takeInt(yield 1);
            }

            function consume(): void {
                \Generator<int, int, int, null> $g = foo();
                $g->send(2);
            }
            """);

        errors.Should().BeEmpty(
            "takeInt(yield) must constrain TSend to int: " + Describe(errors));
    }

    [Fact]
    public void IterableExport_StillAllowsYield_DoesNotGiveCallersSend()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function foo(): iterable {
                yield 1;
            }

            function consume(): void {
                iterable $xs = foo();
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerGeneratorInvalidReturnType,
            "iterable remains a legal weaker export: " + Describe(errors));
        errors.Should().BeEmpty("iterable generator export must typecheck: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content) =>
        CompileChecked(content).Errors;

    private static ICheckedType InferYieldType(TyhpChecker checker, SrcFileAst file, IBase2Ast yieldNode)
    {
        if (checker.ExpressionTypes.TryGetValue(yieldNode, out var cached)
            && cached is not null
            && cached.Kind != CheckedTypeKind.Unresolved)
        {
            return cached;
        }

        var function = FindAllAst<PhpFunctionDeclAst>(file).First();
        var resolveState = new CheckerState { CurrentFileName = file.FileName };
        var state = new CheckerState
        {
            IsInGeneratorContext = true,
            CurrentFileName = file.FileName,
            EnclosingFunction = function.BoundSymbol as FunctionDeclarationSymbol,
            ExpectedReturnType = function.ReturnType is not null
                ? checker.ResolveTypeAnnotation(function.ReturnType, resolveState, isReturnTypePosition: true)
                : null,
        };
        return checker.ResolveExpressionType(yieldNode, state);
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

    private static (TyhpChecker Checker, SrcFileAst File, IReadOnlyList<IDiagnostic> Errors) CompileChecked(
        string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull(
                "bind should succeed: "
                + string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            var errors = result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
            return (checker, result.ParsedFiles![0], errors);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
