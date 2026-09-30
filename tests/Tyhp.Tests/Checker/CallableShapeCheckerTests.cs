using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story271")]
public class CallableShapeCheckerTests
{
    [Fact]
    public void CallableReturnType_OfInlineShape_IsReturnType()
    {
        var (checker, file, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableReturnType<callable(int $i): string> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.DisplayName.Should().Be("string");
    }

    [Fact]
    public void ExtendsUnknownArity_TypeChecksAsBound()
    {
        var errors = Check("""
            <?tyhp
            function pin<TCallable extends callable(...): bool>(TCallable $cb): void {}
            function demo(): void {
                pin(fn(int $n): bool => true);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void DirectUnknownArityParameter_IsError()
    {
        var errors = Check("""
            <?tyhp
            function take(callable(...): bool $cb): void {}
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerCallableAnyArityNotAValueType,
            Describe(errors));
    }

    [Fact]
    public void OptionalEquals_ProducesArityFacets()
    {
        var errors = Check("""
            <?tyhp
            function greet(string $name, int $times = 1): void {}
            function takeOne(callable(string $name): void $fn): void {
                $fn("a");
            }
            function takeTwo(callable(string $name, int $times =): void $fn): void {
                $fn("a", 2);
            }
            function main(): void {
                takeOne(greet(...));
                takeTwo(greet(...));
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void TyhpdefSpeller_EmitsValuelessEqualsForOptional()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = callable(int $i, bool $b = false): string;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Should().ContainSingle().Subject;
        TyhpdefTypeSpeller.Spell(alias.TypeExpression).Should().Be("callable(int $i, bool $b =): string");
    }

    [Fact]
    public void CallableAngleGeneric_DoesNotParseAsCallableType()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function take(callable<string, int> $cb): void {}
            """);

        result.Diagnostics.Errors.Should().NotBeEmpty();
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().BeEmpty();
    }

    [Fact]
    public void ResolvedShape_IsCallableCheckedType_NotGenericCallable()
    {
        var (checker, file, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(callable(int $i): string $cb): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
        var type = ResolveParameterDeclaredType(checker, file, "cb");
        type.Should().BeOfType<CallableCheckedType>();
        var callable = (CallableCheckedType)type;
        callable.IsAnyArity.Should().BeFalse();
        callable.ParameterTypes.Should().ContainSingle().Which.DisplayName.Should().Be("int");
        callable.ReturnType.DisplayName.Should().Be("string");
        callable.ParameterNames.Should().NotBeNull();
        callable.ParameterNames![0].Should().Be("i");
        type.Should().NotBeOfType<GenericCheckedType>();
    }

    [Fact]
    public void GroupedCallableUnionNull_AcceptsFnAndNull()
    {
        var errors = Check("""
            <?tyhp
            function take((callable(int $x): int) | null $cb): void {}
            function demo(): void {
                take(fn(int $x): int => $x);
                take(null);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void PrefixNullableCallable_AcceptsNull_NullableReturn_DoesNot()
    {
        var prefixOk = Check("""
            <?tyhp
            function take(?callable(int $x): string $cb): void {}
            function demo(): void {
                take(null);
                take(fn(int $x): string => "ok");
            }
            """);

        prefixOk.Should().BeEmpty(Describe(prefixOk));

        var nullableReturnOk = Check("""
            <?tyhp
            function take(callable(int $x): ?string $cb): void {}
            function demo(): void {
                take(fn(int $x): ?string => null);
            }
            """);

        nullableReturnOk.Should().BeEmpty(Describe(nullableReturnOk));

        var nullNotAssignableToShape = Check("""
            <?tyhp
            function take(callable(int $x): ?string $cb): void {}
            function demo(): void {
                take(null);
            }
            """);

        nullNotAssignableToShape.Should().NotBeEmpty(Describe(nullNotAssignableToShape));
    }

    private static IReadOnlyList<IDiagnostic> Check(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static (TyhpChecker Checker, SrcFileAst File, DiagnosticBag Diagnostics) CompileForChecker(
        string content)
    {
        var result = IsolatedCompilation.ParseSnippet(content, skipChecking: true);
        result.GlobalScope.Should().NotBeNull();
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return (checker, result.ParsedFiles![0], result.Diagnostics);
    }

    private static ICheckedType ResolveParameterDeclaredType(
        TyhpChecker checker,
        SrcFileAst file,
        string parameterName)
    {
        var function = Flatten(file).OfType<PhpFunctionDeclAst>()
            .First(decl => string.Equals(decl.Identifier, "demo", StringComparison.Ordinal));
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
}
