using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 27.1 Phase 4 — <c>\Closure</c> / <c>\Fiber</c> / <c>Expression</c> / FCC
/// inference with callable shapes.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story271")]
public class CallableShapeClosureInferenceTests
{
    [Fact]
    public void Closure_OneArgCallableShape_TypeChecksWithThisAndScopeDefaults()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            function take(\Closure<callable(int $i): string> $c): string {
                return $c(1);
            }

            function demo(): void {
                take(fn (int $x): string => "a");
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = ResolveParameterDeclaredType(checker, file, "take", "c");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        generic.TypeArguments.Should().HaveCount(3, type.DisplayName);
        generic.TypeArguments[0].DisplayName.Should().Be("callable(int $i): string", type.DisplayName);
        type.DisplayName.Should().NotContain("Closure(int)", type.DisplayName);
        type.DisplayName.Should().NotMatchRegex(@"Closure\s*\(");
    }

    [Fact]
    public void Fiber_CallableShape_TypeChecksAsTCallableShape()
    {
        var errors = Compile("""
            <?tyhp
            function demo(\Fiber<mixed, callable(int $i): string> $fiber): void {}
            """).Errors;

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Expression_TypedLocal_NamedCallableShape_StillCaptures()
    {
        var errors = Compile("""
            <?tyhp
            class User {
                public int $age;
            }

            function demo(): void {
                \Tyhp\Expression<callable(User $u): bool> $e = fn ($u) => $u->age > 18;
            }
            """).Errors;

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Expression_TypedLocal_UnassignedCapture_Reports4324()
    {
        var errors = Compile("""
            <?tyhp
            class User {
                public int $age;
            }

            function demo(): void {
                int $minAge;
                \Tyhp\Expression<callable(User $u): bool> $e = fn ($u) => $u->age > $minAge;
            }
            """).Errors;

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerExpressionCapturedVarUndefined,
            Describe(errors));
    }

    [Fact]
    public void StrlenFirstClassCallable_InfersNamedCallableShape()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            function demo(): void {
                $c = strlen(...);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
        var type = InferredTypeOfFirstFcc(checker, file);
        ShapeSlot(type).Should().Be("callable(string $string): int", type.DisplayName);
        type.DisplayName.Should().NotContain("Closure(int)", type.DisplayName);
        type.DisplayName.Should().NotMatchRegex(@"Closure\s*\(");
    }

    [Fact]
    public void ClosureAnnotation_DoesNotUsePhpStanSpelling()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            function demo(\Closure<callable(int $i): string> $c): void {}
            """);

        errors.Should().BeEmpty(Describe(errors));
        var declared = ResolveParameterDeclaredType(checker, file, "demo", "c");
        declared.DisplayName.Should().Contain("callable(int $i): string", declared.DisplayName);
        declared.DisplayName.Should().NotContain("Closure(int)", declared.DisplayName);
        declared.DisplayName.Should().NotMatchRegex(@"Closure\s*\(");
    }

    private static string ShapeSlot(ICheckedType type) =>
        type is GenericCheckedType { TypeArguments.Count: > 0 } g
            ? g.TypeArguments[0].DisplayName
            : type.DisplayName;

    private static ICheckedType InferredTypeOfFirstFcc(TyhpChecker checker, SrcFileAst file)
    {
        var call = Flatten(file).OfType<PhpCallAst>().First(c =>
            CheckerHelpers.IsFirstClassCallableArgumentList(c.Arguments));
        var deref = Flatten(file).OfType<PhpDereferenceableAst>()
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

    private static ICheckedType ResolveParameterDeclaredType(
        TyhpChecker checker,
        SrcFileAst file,
        string functionName,
        string parameterName)
    {
        var function = Flatten(file).OfType<PhpFunctionDeclAst>()
            .First(decl => string.Equals(decl.Identifier, functionName, StringComparison.Ordinal));
        var parameter = function.Parameters!.GetAllNotNull()
            .First(param => string.Equals(param.Name.TrimStart('$'), parameterName, StringComparison.Ordinal));
        var state = new CheckerState { CurrentFileName = file.FileName };
        if (function.BoundSymbol is FunctionDeclarationSymbol functionSymbol)
        {
            state.EnclosingFunction = functionSymbol;
            state.IsParameterTypePosition = true;
            if (functionSymbol.GenericParameters.Count > 0)
            {
                state.FunctionGenerics = functionSymbol.GenericParameters;
            }
        }

        return checker.ResolveTypeAnnotation(parameter.Type!, state);
    }

    private static IEnumerable<IBase2Ast> Flatten(IBase2Ast? node)
    {
        if (node is null)
        {
            yield break;
        }

        yield return node;
        foreach (var child in node.AstChildren ?? [])
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static (TyhpChecker Checker, SrcFileAst File, IReadOnlyList<IDiagnostic> Errors) Compile(string content)
    {
        var result = IsolatedCompilation.ParseSnippet(content, skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        var errors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (checker, result.ParsedFiles![0], errors);
    }
}
