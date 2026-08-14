using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class Phase10_5EmitterTests
{
    [Fact]
    public void TypeAliasMap_UsesPhpSpellingForNamespacedTarget()
    {
        var result = Compile("""
            <?tyhp
            namespace App\Models;
            class Bar {}
            namespace App\Services;
            use App\Models\Bar;
            type Handler = Bar;
            """);

        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
        context.TypeAliasMap.Should().ContainKey("Handler");
        context.TypeAliasMap["Handler"].Should().Be(@"\App\Models\Bar");
    }

    [Fact]
    public void TypeAliasMap_ResolvesBuiltinAliasTarget()
    {
        var result = Compile("""
            <?tyhp
            type UserId = int;
            """);

        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
        context.TypeAliasMap["UserId"].Should().Be("int");
    }

    [Fact]
    public void OperatorOverloadResolver_PicksMostSpecificBinaryForm()
    {
        var result = Compile("""
            <?tyhp
            class Money {
                operator +(self $left, self $right) { return $left; }
                operator +(self $left, int $right) { return $left; }
            }
            """);

        var money = FindClass(result.GlobalScope!, "Money");
        var candidates = GetClassOperatorOverloads(result.GlobalScope!, money).ToList();
        candidates.Should().HaveCount(2);

        var moneyOperand = ParseRhsExpression("$m");
        var intLiteral = ParseRhsExpression("1");

        // (Money, int) selects the `(self, int)` form, not `(self, self)`.
        var match = OperatorOverloadResolver.SelectMatchingBinaryForm(
            candidates,
            OverloadableOperator.Add,
            moneyOperand,
            intLiteral,
            money,
            expr => ReferenceEquals(expr, moneyOperand) ? money : null,
            expr => ReferenceEquals(expr, moneyOperand) ? "Money" : "Int");

        match.Should().NotBeNull();
        match!.Parameters.Should().HaveCount(2);
        GetPrimaryTypeName(match.Parameters[1].DeclaredType).Should().Be("int");
    }

    [Fact]
    public void OperatorOverloadResolver_FallsBackToFirstArityMatchForUnresolvedVariables()
    {
        var result = Compile("""
            <?tyhp
            class Money {}
            extension MoneySelfAdd {
                operator +<Money>(self $left, self $right): self { return $left; }
            }
            extension MoneyIntAdd {
                operator +<Money>(self $left, int $right): self { return $left; }
            }
            """);

        var money = FindClass(result.GlobalScope!, "Money");
        var left = ParseRhsExpression("$x");
        var right = ParseRhsExpression("$y");

        // Both operands unresolved: fall back to the first arity match.
        var match = OperatorOverloadResolver.SelectMatchingBinaryForm(
            money.ExtensionContributedOperators,
            OverloadableOperator.Add,
            left,
            right,
            money,
            _ => null,
            _ => "Mixed");

        match.Should().NotBeNull();
        match!.Parameters[1].DeclaredType.Should().NotBeNull();
        GetPrimaryTypeName(match.Parameters[1].DeclaredType).Should().Be("self");
    }

    [Fact]
    public void OperatorOverloadResolver_SkipsCandidatesWithWrongArity()
    {
        var result = Compile("""
            <?tyhp
            class Box {
                operator +(self $value) { return $value; }
            }
            """);

        var box = FindClass(result.GlobalScope!, "Box");
        var left = ParseRhsExpression("1");
        var right = ParseRhsExpression("2");
        var candidates = GetClassOperatorOverloads(result.GlobalScope!, box);

        // A unary `+` form cannot satisfy a binary `+` call site.
        var match = OperatorOverloadResolver.SelectMatchingBinaryForm(
            candidates,
            OverloadableOperator.Add,
            left,
            right,
            box,
            _ => null,
            _ => "Int");

        match.Should().BeNull();
    }

    [Fact]
    public void OperatorOverloadResolver_ResolvesEachOperandPositionIndependently()
    {
        // Redesign: both operands are real parameters, so `(self, int)` and `(int, self)` are
        // distinguished purely by which position holds the Money operand (no self-on-left fallback).
        var result = Compile("""
            <?tyhp
            class Money {
                operator +(self $left, int $right) { return $left; }
                operator +(int $left, self $right) { return $right; }
            }
            """);

        var money = FindClass(result.GlobalScope!, "Money");
        var candidates = GetClassOperatorOverloads(result.GlobalScope!, money).ToList();
        candidates.Should().HaveCount(2);

        var moneyOperand = ParseRhsExpression("$m");
        var intLiteral = ParseRhsExpression("1");

        IBaseSymbol? Resolve(IExpression? expr) => ReferenceEquals(expr, moneyOperand) ? money : null;
        string Guess(IExpression? expr) => ReferenceEquals(expr, moneyOperand) ? "Money" : "Int";

        // (Money, int) → `(self, int)` form.
        var leftFirst = OperatorOverloadResolver.SelectMatchingBinaryForm(
            candidates, OverloadableOperator.Add, moneyOperand, intLiteral, money, Resolve, Guess);
        leftFirst.Should().NotBeNull();
        GetPrimaryTypeName(leftFirst!.Parameters[0].DeclaredType).Should().Be("self");
        GetPrimaryTypeName(leftFirst.Parameters[1].DeclaredType).Should().Be("int");

        // (int, Money) → `(int, self)` form.
        var rightForm = OperatorOverloadResolver.SelectMatchingBinaryForm(
            candidates, OverloadableOperator.Add, intLiteral, moneyOperand, money, Resolve, Guess);
        rightForm.Should().NotBeNull();
        GetPrimaryTypeName(rightForm!.Parameters[0].DeclaredType).Should().Be("int");
        GetPrimaryTypeName(rightForm.Parameters[1].DeclaredType).Should().Be("self");
    }

    private static CompilationResult Compile(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-phase10_5", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        using var compilationService = new CompilationService();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            PhpVersion = "8.4",
            ProjectPath = tempDir,
        };

        return compilationService.ParseFiles([filePath], options);
    }

    private static ObjectDeclarationSymbol FindClass(GlobalScope global, string name)
    {
        return EnumerateScopes(global)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .First(symbol => string.Equals(symbol.Name, name, StringComparison.Ordinal));
    }

    private static IEnumerable<IBaseScope> EnumerateScopes(IBaseScope root)
    {
        yield return root;
        foreach (var child in root.GetAllChildScopes())
        {
            foreach (var descendant in EnumerateScopes(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<ObjectOperatorOverloadMethodSymbol> GetClassOperatorOverloads(
        GlobalScope global,
        ObjectDeclarationSymbol typeSymbol)
    {
        var objectScope = FindObjectDeclarationScope(global, typeSymbol);
        if (objectScope != null)
        {
            return ((IBaseScope)objectScope).GetAllChildSymbols().OfType<ObjectOperatorOverloadMethodSymbol>();
        }

        return typeSymbol.Members.Values.OfType<ObjectOperatorOverloadMethodSymbol>();
    }

    private static ObjectDeclarationScope? FindObjectDeclarationScope(
        IBaseScope scope,
        ObjectDeclarationSymbol typeSymbol)
    {
        if (scope is ObjectDeclarationScope objectScope
            && ReferenceEquals(objectScope.DeclarationSymbol, typeSymbol))
        {
            return objectScope;
        }

        foreach (var childScope in scope.GetAllChildScopes())
        {
            var found = FindObjectDeclarationScope(childScope, typeSymbol);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string? GetPrimaryTypeName(ITypeExpression? typeExpression)
    {
        return typeExpression switch
        {
            PhpBuiltinTypeAst builtin => builtin.Identifier,
            PhpNamedTypeAst named => named.Name?.ValueString,
            PhpTypeExpressionAst composite =>
                composite.Types?.GetAllNotNull().FirstOrDefault() is ITypeExpression inner
                    ? GetPrimaryTypeName(inner)
                    : null,
            _ => null,
        };
    }

    private static IExpression ParseRhsExpression(string expression)
    {
        var parseResult = ParserTestHelper.ParseTyhpContent($"<?tyhp\nmixed $v = {expression};\n");
        parseResult.Diagnostics.HasErrors.Should().BeFalse();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var assignment = srcFile.AstChildren
            .OfType<PhpTopStatementListAst>()
            .SelectMany(list => list.GetAllNotNull())
            .OfType<TyhpTypedVarExprAst>()
            .First(stmt => stmt.AssignedExpression != null);
        return assignment.AssignedExpression!;
    }
}
