using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase10_5RuleTests
{
    [Fact]
    public void Check_EnumCaseNameWithNonEnumType_ReportsGenericConstraintNotSatisfied()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class NotAnEnum {}

            function demo(__EnumCaseName<NotAnEnum> $name): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_MethodNameWithPrimitiveType_ReportsGenericConstraintNotSatisfied()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__MethodName<int> $name): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_PickWithPrimitiveType_ReportsGenericConstraintNotSatisfied()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__Pick<int, 'x'> $picked): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_PartialWithPrimitiveType_ReportsGenericConstraintNotSatisfied()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__Partial<int> $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_WellConstrainedSymbolNameTypes_Succeed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {}

            function demo(__MethodName<Widget> $methodName): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_PropertyNameLiteral_AcceptsBareNameWithoutDollar()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function demo(): void {
                __PropertyName<Widget> $prop = 'name';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_UsedTraitNameLiteral_AcceptsUsedTrait()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasId {}

            class Widget {
                use HasId;
            }

            function demo(): void {
                __UsedTraitName<Widget> $trait = 'HasId';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_UtilityArityErrorTakesPrecedenceOverConstraintError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__Pick<int> $picked): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_ReturnTypeWithNonCallable_ReportsConstraintOnce()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__CallableReturnType<int> $t): void {}
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUtilityTypeInvalidArgument);
    }

    [Fact]
    public void Check_ParametersWithNonCallable_ReportsConstraintOnce()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__CallableParametersTuple<string> $params): void {}
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUtilityTypeInvalidArgument);
    }

    [Fact]
    public void Check_ReturnTypeWithTypedCallable_ResolvesWithoutConstraintError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__CallableReturnType<callable(string): int> $t): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUtilityTypeInvalidArgument);
    }

    [Fact]
    public void SatisfiesCallableConstraint_RejectsGenericCallableConstruction()
    {
        // `callable` is not a generic type. Leftover `GenericCheckedType` wrappers must not
        // satisfy Callable; shapes use `CallableCheckedType`.
        var emptyCallable = new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable")),
            []);
        var typedCallable = new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable")),
            [CheckedTypes.Int]);
        var shape = new CallableCheckedType([CheckedTypes.Int], CheckedTypes.String);

        GenericTypeArgumentValidator.SatisfiesCallableConstraint(emptyCallable).Should().BeFalse();
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(typedCallable).Should().BeFalse();
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(shape).Should().BeTrue();
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(
            CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable"))).Should().BeTrue();
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(CheckedTypes.Int).Should().BeFalse();
    }

    [Fact]
    public void Check_ReadonlyStruct_MarksAllPropertiesReadonly()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(__AsReadOnly<Point> $point): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "point");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().HaveCount(2);
        structType.Properties.Values.Should().OnlyContain(property => property.IsReadonly);
    }

    [Fact]
    public void Check_ReadonlyStruct_Inherited_IncludesParentProperties()
    {
        // Regression for a `StructTypeHelper.BuildFromObjectDeclaration` gap found while reviewing
        // Story 11 struct #1/#6: `extends` is parsed as a raw `IClassName`, not an `ITypeExpression`,
        // so `ObjectDeclarationSymbol.ExtendsType` is null for a normal struct declaration and the
        // parent must be resolved via `TypeComparer.TryGetParentDeclaration`'s AST fallback. Without
        // walking that chain, `__AsReadOnly<ChildShape>` (and `Pick`/`Omit`/`Partial`/`keyof`) only
        // saw the child's own properties, silently dropping every inherited one.
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type ParentShape = struct {
                int $count = 0;
            };
            type ChildShape = struct extends ParentShape  {
                string $name = '';
            };

            function demo(__AsReadOnly<ChildShape> $value): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "value");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().HaveCount(2);
        structType.Properties.Should().ContainKey("$count");
        structType.Properties.Should().ContainKey("$name");
        structType.Properties.Values.Should().OnlyContain(property => property.IsReadonly);
    }

    [Fact]
    public void Check_ReadonlyClass_MarksAllPropertiesReadonly()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            class Widget {
                public string $name;
                public int $age;
            }

            function demo(__AsReadOnly<Widget> $value): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "value");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().HaveCount(2);
        structType.Properties.Values.Should().OnlyContain(property => property.IsReadonly);
    }

    [Fact]
    public void Check_PartialStruct_MarksAllPropertiesOptionalAndNullable()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(__Partial<Point> $point): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "point");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().HaveCount(2);
        structType.Properties.Values.Should().OnlyContain(property => property.IsOptional);
        structType.Properties.Values.Should().OnlyContain(property => property.Type.IsNullable);
    }

    [Fact]
    public void Check_PartialStruct_AllowsOmittingKeys()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(): void {
                __Partial<Point> $point = ['x' => 1];
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_RequiredStruct_ClearsNullability()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                ?int $x = null;
                ?string $y = null;
            };

            function demo(__Required<Point> $point): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "point");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().HaveCount(2);
        structType.Properties.Values.Should().OnlyContain(property => !property.IsOptional);
        structType.Properties.Values.Should().OnlyContain(property => !property.Type.IsNullable);
    }

    [Fact]
    public void Check_PickStruct_KeepsOnlyNamedKeys()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(__Pick<Point, 'x'> $picked): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "picked");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().ContainKey("$x");
        structType.Properties.Should().NotContainKey("$y");
    }

    [Fact]
    public void Check_OmitStruct_DropsNamedKeys()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(__Omit<Point, 'y'> $omitted): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "omitted");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().ContainKey("$x");
        structType.Properties.Should().NotContainKey("$y");
    }

    [Fact]
    public void Check_StructKeyWithStruct_DoesNotApplyReadonlyFlags()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
            };

            function demo(__StructKey<Point> $key): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();

        var type = ResolveParameterDeclaredType(checker, file, "key");
        type.Should().NotBeOfType<StructCheckedType>();
    }

    private static ICheckedType ResolveParameterDeclaredType(TyhpChecker checker, SrcFileAst file, string parameterName)
    {
        var function = FindAllAst<PhpFunctionDeclAst>(file)
            .FirstOrDefault(decl => string.Equals(decl.Identifier, "demo", StringComparison.Ordinal));
        function.Should().NotBeNull("demo function should exist");

        var parameter = function!.Parameters?.GetAllNotNull()
            .FirstOrDefault(param =>
                string.Equals(param.Name.TrimStart('$'), parameterName, StringComparison.Ordinal));
        parameter.Should().NotBeNull($"parameter '{parameterName}' should exist");
        parameter!.Type.Should().NotBeNull();

        var state = new CheckerState { CurrentFileName = file.FileName };
        if (function.BoundSymbol is FunctionDeclarationSymbol functionSymbol)
        {
            state.EnclosingFunction = functionSymbol;
        }

        return checker.ResolveTypeAnnotation(parameter.Type!, state);
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

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var (_, _, _, diagnostics) = CompileForChecker(content);
        return diagnostics;
    }

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global, DiagnosticBag diagnostics) CompileForChecker(string content)
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
            return (checker, result.ParsedFiles![0], result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
