using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.Binder;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class VariableStateTests
{
    [Fact]
    public void ForParameter_CreatesDefinitelyAssignedReferenceState()
    {
        var param = new VariableSymbol("$x") { IsParameter = true, IsRef = true };
        var state = VariableState.ForParameter(param, CheckedTypes.Int, isReference: true);

        state.IsDefinitelyAssigned.Should().BeTrue();
        state.IsParameter.Should().BeTrue();
        state.IsReference.Should().BeTrue();
        state.ReferenceGroup.Should().NotBeNull();
        state.ReferenceGroup!.MemberVariables.Should().Contain("$x");
    }

    [Fact]
    public void Clone_ProducesIndependentScalarFieldsButSharedReferenceGroup()
    {
        var original = VariableState.ForParameter(new VariableSymbol("$a"), CheckedTypes.Int, isReference: true);
        var clone = original.Clone();

        clone.IsDefinitelyAssigned = false;
        original.IsDefinitelyAssigned.Should().BeTrue();
        clone.ReferenceGroup.Should().BeSameAs(original.ReferenceGroup);
    }
}

[Trait("Category", "Checker")]
public class CheckerStateVariableTests
{
    [Fact]
    public void DeclareVariable_RegistersInFunctionScope()
    {
        var diagnostics = new DiagnosticBag();
        var root = new CheckerState { ScopeType = ScopeType.File };
        var function = root.Split(ScopeType.FunctionDeclaration);
        var block = function.Split(ScopeType.CodeBlock);
        var symbol = new VariableSymbol("count");

        block.DeclareVariable("count", symbol, CheckedTypes.Int, isAssigned: false, diagnostics);

        function.Variables.Should().ContainKey("count");
        block.LookupVariable("count").Should().NotBeNull();
        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void DeclareVariable_DuplicateInSameScope_ReportsError()
    {
        var diagnostics = new DiagnosticBag();
        var state = new CheckerState { ScopeType = ScopeType.FunctionDeclaration };
        var symbol = new VariableSymbol("x") { SourceFile = "test.tyhp" };

        state.DeclareVariable("x", symbol, CheckedTypes.Int, isAssigned: false, diagnostics);
        state.DeclareVariable("x", symbol, CheckedTypes.Int, isAssigned: false, diagnostics);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void LookupVariable_StopsAtFunctionBoundary()
    {
        var outer = new CheckerState { ScopeType = ScopeType.FunctionDeclaration };
        outer.Variables["outer"] = new VariableState { DeclaredType = CheckedTypes.Int };

        var inner = outer.Split(ScopeType.FunctionDeclaration);
        inner.Variables["inner"] = new VariableState { DeclaredType = CheckedTypes.String };

        inner.LookupVariable("inner").Should().NotBeNull();
        inner.LookupVariable("outer").Should().BeNull();
    }

    [Fact]
    public void NarrowVariable_UpdatesEffectiveType()
    {
        var state = new CheckerState { ScopeType = ScopeType.FunctionDeclaration };
        state.Variables["item"] = new VariableState { DeclaredType = CheckedTypes.Mixed };

        state.NarrowVariable("item", CheckedTypes.String);

        state.LookupVariable("item")!.EffectiveType.Should().Be(CheckedTypes.String);
    }

    [Fact]
    public void AssignVariable_UpdatesAssignmentState()
    {
        var diagnostics = new DiagnosticBag();
        var state = new CheckerState { ScopeType = ScopeType.FunctionDeclaration };
        state.Variables["value"] = new VariableState { DeclaredType = CheckedTypes.Mixed };

        state.AssignVariable("value", CheckedTypes.Int, diagnostics);

        var variable = state.LookupVariable("value");
        variable!.IsDefinitelyAssigned.Should().BeTrue();
        variable.EffectiveType.Should().Be(CheckedTypes.Int);
    }
}

[Trait("Category", "Checker")]
public class TypeInferrerTests
{
    [Fact]
    public void InferExpressionType_IntegerAddition_ReturnsInt()
    {
        var (checker, file, _) = CompileForChecker("""
            <?tyhp
            namespace Test;
            function math(): int {
                return 1 + 2;
            }
            """);

        var binary = FindBinaryOperator(file, PhpBinaryOperator.Plus);
        binary.Should().NotBeNull();

        var type = checker.ResolveExpressionType(binary!, new CheckerState());
        type.Should().Be(CheckedTypes.Int);
        checker.ExpressionTypes.Should().ContainKey(binary!);
    }

    [Fact]
    public void InferExpressionType_Comparison_ReturnsBool()
    {
        var (checker, file, _) = CompileForChecker("""
            <?tyhp
            function compare(): bool {
                return 1 === 2;
            }
            """);

        var binary = FindBinaryOperator(file, PhpBinaryOperator.Identical);
        binary.Should().NotBeNull();
        checker.ResolveExpressionType(binary!, new CheckerState()).Should().Be(CheckedTypes.Bool);
    }

    [Fact]
    public void ResolveTypeAnnotation_UnionType_ResolvesMembers()
    {
        var (checker, file, global) = CompileForChecker("""
            <?tyhp
            function demo(int|string $value): void {}
            """);

        var function = FindFirstAst<PhpFunctionDeclAst>(file);
        function.Should().NotBeNull();
        var paramType = function!.Parameters!.GetAllNotNull().First().Type;
        paramType.Should().NotBeNull();

        var state = new CheckerState
        {
            EnclosingFunction = function.BoundSymbol as FunctionDeclarationSymbol,
            CurrentFileName = file.FileName,
        };

        var resolved = checker.ResolveTypeAnnotation(paramType!, state);
        resolved.Should().BeOfType<UnionCheckedType>();
        ((UnionCheckedType)resolved).Members.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveTypeAnnotation_NullableType_IsNullable()
    {
        var (checker, file, _) = CompileForChecker("""
            <?tyhp
            function demo(?string $value): void {}
            """);

        var function = FindFirstAst<PhpFunctionDeclAst>(file);
        var paramType = function!.Parameters!.GetAllNotNull().First().Type!;

        var resolved = checker.ResolveTypeAnnotation(paramType, new CheckerState { CurrentFileName = file.FileName });
        resolved.IsNullable.Should().BeTrue();
        resolved.Should().BeOfType<NullableCheckedType>();
    }

    [Fact]
    public void InferExpressionType_ErrorAst_ReturnsUnknownWithoutDiagnostics()
    {
        var diagnostics = new DiagnosticBag();
        var symbolTree = new SymbolTree(new SymbolIdentifier([]));
        var checker = new TyhpChecker(diagnostics, symbolTree, symbolTree.GlobalScope);
        var error = ErrorAst.Create("broken", MessageCode.VisitorUnknownError, 1, 0);

        var type = checker.ResolveExpressionType(error, new CheckerState());

        type.Should().Be(CheckedTypes.Unresolved);
        diagnostics.HasErrors.Should().BeFalse();
    }

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global) CompileForChecker(string content)
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
            var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
            bindErrors.Should().BeEmpty(
                $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");
            result.GlobalScope.Should().NotBeNull();
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var file = result.ParsedFiles![0];
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (checker, file, result.GlobalScope!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static PhpBinaryOpAst? FindBinaryOperator(IBase2Ast root, PhpBinaryOperator op)
    {
        foreach (var binary in FindAllAst<PhpBinaryOpAst>(root))
        {
            var token = binary.Operator?.TokenValue ?? -1;
            if (PhpBinaryOperatorExtensions.FromToken(token) == op)
            {
                return binary;
            }
        }

        return null;
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

    private static T? FindFirstAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            var found = FindFirstAst<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
