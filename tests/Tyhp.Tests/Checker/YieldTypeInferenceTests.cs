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
/// <c>yield</c> / <c>yield $v</c> / <c>yield $k => $v</c> infer as the enclosing
/// <c>Generator</c>'s TSend (PHP <c>send()</c> value). <c>yield from</c> infers as the
/// inner generator's TReturn (<c>getReturn()</c>), or <c>null</c> for non-Generator
/// iterables.
/// </summary>
[Trait("Category", "Checker")]
public class YieldTypeInferenceTests
{
    [Fact]
    public void YieldValue_InfersEnclosingTSend()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator<int, string, bool, mixed> {
                bool $sent = yield "a";
            }
            """);

        errors.Should().BeEmpty(
            "yield $v must type as TSend (bool), not TValue/unresolved: " + Describe(errors));
    }

    [Fact]
    public void YieldValue_RejectsWhenAssignedToTValueInsteadOfTSend()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator<int, string, bool, mixed> {
                string $sent = yield "a";
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "yield $v is TSend=bool, not TValue=string (unresolved would assign to string): "
            + Describe(errors));
    }

    [Fact]
    public void YieldKeyValue_InfersEnclosingTSend()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator<int, string, bool, mixed> {
                bool $sent = yield 1 => "a";
            }
            """);

        errors.Should().BeEmpty(
            "yield $k => $v must type as TSend: " + Describe(errors));
    }

    [Fact]
    public void BareYieldStatement_InfersEnclosingTSend()
    {
        // `$x = yield;` does not parse (phpExprYieldValue requires a value operand).
        // Statement `yield;` is unary T_YIELD — InferUnary's leftover.
        var (checker, file, errors) = CompileChecked("""
            <?tyhp
            function gen(): \Generator<int, string, int, mixed> {
                yield;
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "bare yield statement must type-check: " + Describe(errors));

        var unary = FindBareYield(file);
        unary.Should().NotBeNull("statement yield; must be PhpUnaryOpAst");
        InferYieldType(checker, file, unary!).DisplayName.Should().Be(
            "int",
            "bare yield must type as TSend, not unresolved");
    }

    [Fact]
    public void BareYieldStatement_IsNotUnresolvedOrTValue()
    {
        var (checker, file, _) = CompileChecked("""
            <?tyhp
            function gen(): \Generator<int, string, int, mixed> {
                yield;
            }
            """);

        var typeName = InferYieldType(checker, file, FindBareYield(file)!).DisplayName;
        typeName.Should().NotBe("unresolved");
        typeName.Should().NotBe("string", "TSend is int, not TValue");
    }

    [Fact]
    public void Yield_BareGeneratorReturn_TypesAsMixed()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                mixed $sent = yield 1;
            }
            """);

        errors.Should().BeEmpty(
            "bare Generator has no TSend slot — mixed, matching TReturn fallback: "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_Generator_InfersInnerTReturn()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(\Generator<int, string, mixed, float> $inner): \Generator {
                float $ret = yield from $inner;
            }
            """);

        errors.Should().BeEmpty(
            "yield from Generator<…, TReturn> must type as TReturn: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_Generator_RejectsWhenAssignedToTValueInsteadOfTReturn()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(\Generator<int, string, mixed, float> $inner): \Generator {
                string $ret = yield from $inner;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "yield from is inner TReturn=float, not TValue=string: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_Array_InfersNull()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                null $done = yield from [1, 2, 3];
            }
            """);

        errors.Should().BeEmpty(
            "yield from array evaluates to null in PHP: " + Describe(errors));
    }

    [Fact]
    public void YieldFrom_Array_RejectsIntAssignment()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(): \Generator {
                int $done = yield from [1, 2, 3];
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "yield from array is null, not unresolved (which would assign to int): "
            + Describe(errors));
    }

    [Fact]
    public void YieldFrom_TwoArgGenerator_TReturnDefaultsToMixed()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function gen(\Generator<int, string> $inner): \Generator {
                mixed $ret = yield from $inner;
            }
            """);

        errors.Should().BeEmpty(
            "Generator<K,V> omits TSend/TReturn — yield from is mixed, not null: "
            + Describe(errors));
    }

    [Fact]
    public void NestedGeneratorClosure_UsesOwnTSendNotOuter()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function outer(): \Generator<int, string, bool, mixed> {
                bool $outerSent = yield "a";
                $inner = function(): \Generator<int, string, int, mixed> {
                    int $innerSent = yield "b";
                };
            }
            """);

        errors.Should().BeEmpty(
            "generator closure must use its own TSend, not the outer function's: "
            + Describe(errors));
    }

    [Fact]
    public void NestedGeneratorClosure_RejectsOuterTSendAssignment()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function outer(): \Generator<int, string, bool, mixed> {
                yield "a";
                $inner = function(): \Generator<int, string, int, mixed> {
                    bool $innerSent = yield "b";
                };
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "inner generator TSend is int; assigning yield to bool would pass if outer TSend leaked: "
            + Describe(errors));
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

    private static PhpUnaryOpAst? FindBareYield(IBase2Ast root) =>
        FindAllAst<PhpUnaryOpAst>(root).FirstOrDefault(unary =>
        {
            var op = unary.Operator?.ValueString ?? "";
            var isYieldFrom = op.Contains("yield", StringComparison.OrdinalIgnoreCase)
                && op.Contains("from", StringComparison.OrdinalIgnoreCase);
            return !isYieldFrom
                && string.Equals(op, "yield", StringComparison.OrdinalIgnoreCase)
                && unary.Operand is null;
        });

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
            result.GlobalScope.Should().NotBeNull("bind should succeed");
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
