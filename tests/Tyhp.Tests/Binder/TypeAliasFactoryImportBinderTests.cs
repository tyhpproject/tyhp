using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhp")]
public class TypeAliasFactoryImportBinderTests
{
    [Fact]
    public void Bind_ClassKindUseOfAlias_ImportsTypeAliasForTypePosition()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                function take(UserId $id): void {}
            }
            """);

        diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        UseIncludeSymbol? useSymbol = null;
        Walk(global!, symbol =>
        {
            if (useSymbol is null
                && symbol is UseIncludeSymbol use
                && string.Equals(use.Name, "UserId", StringComparison.OrdinalIgnoreCase)
                && use.UseType == PhpUseType.Class)
            {
                useSymbol = use;
            }
        });
        useSymbol.Should().NotBeNull();
        useSymbol!.ImportedName.Should().Be("App\\Types\\UserId");

        var take = FindFunction(global!, "take");
        var bound = FindBoundTypeAlias(take.Parameters[0].DeclaredType);
        bound.Should().BeOfType<TypeAliasSymbol>().Which.Name.Should().Be("UserId");
    }

    private static FunctionDeclarationSymbol FindFunction(GlobalScope global, string name)
    {
        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found is null
                && symbol is FunctionDeclarationSymbol fn
                && string.Equals(fn.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = fn;
            }
        });
        found.Should().NotBeNull($"expected to find function '{name}'");
        return found!;
    }

    private static TypeAliasSymbol? FindBoundTypeAlias(IBase2Ast? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.BoundSymbol is TypeAliasSymbol alias)
        {
            return alias;
        }

        foreach (var child in node.AstChildren)
        {
            var found = FindBoundTypeAlias(child);
            if (found != null)
            {
                return found;
            }
        }

        foreach (var addon in node.AstGrammarAddons.Values)
        {
            var found = FindBoundTypeAlias(addon);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> onSymbol)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            onSymbol(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, onSymbol);
        }
    }
}
