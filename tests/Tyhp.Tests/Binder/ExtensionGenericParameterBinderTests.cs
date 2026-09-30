using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Standalone tyhpdef <c>extension Name&lt;T&gt;</c> type parameters stay on the extension
/// symbol and resolve by walking that scope. Generics written on a method
/// (<c>fn keys&lt;TKey, TValue&gt;</c>) land on the method symbol. Either way
/// <c>array&lt;TKey, TValue&gt;</c> binds instead of an unresolved type.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class ExtensionGenericParameterBinderTests
{
    [Fact]
    public void StandaloneTyhpdefExtension_RegistersMethodGenerics()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            namespace Test;
            extension Arr<TKey, TValue> extends array<TKey, TValue> {
                fn keys(): array<int, TKey>
                    => \array_keys($this);
            }
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var extension = FindObject(global, "Arr");
        extension.Should().NotBeNull();
        extension!.GenericParameters.Select(gp => gp.Name).Should().Equal("TKey", "TValue");

        var method = FindMethod(global, "Arr", "keys");
        method.Should().NotBeNull();
        method!.GenericParameters.Should().BeEmpty(
            "header type parameters stay on the extension symbol; scope walk resolves them");

        var tKey = extension.GenericParameters.Single(gp => gp.Name == "TKey");
        var tValue = extension.GenericParameters.Single(gp => gp.Name == "TValue");
        AssertNamedTypesBoundTo(extension.DeclaringAstNode, "TKey", tKey);
        AssertNamedTypesBoundTo(extension.DeclaringAstNode, "TValue", tValue);
    }

    [Fact]
    public void ClassBodyInlineExtension_RegistersMethodGenerics()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            namespace Test;
            class Box {
                extension fn keys<TKey, TValue>(): array<int, TKey> => \array_keys($this);
            }
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var box = FindObject(global, "Box");
        box.Should().NotBeNull();
        var synth = box!.SyntheticInlineExtension;
        synth.Should().NotBeNull();

        var method = synth!.Members.TryGetValue("keys", out var member)
            ? member as ObjectMethodSymbol
            : null;
        method.Should().NotBeNull();
        method!.GenericParameters.Select(gp => gp.Name).Should().Equal("TKey", "TValue");
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) BindFixture(
        string tyhpdef,
        string tyhp)
    {
        var result = Compile(tyhpdef, tyhp);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static CompilationResult Compile(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "ext.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "demo.tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            return compilationService.ParseFiles([tyhpPath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = tempDir,
                TyhpdefIncludePaths = ["ext.tyhpdef"],
                SkipChecking = true,
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static ObjectMethodSymbol? FindMethod(GlobalScope global, string typeName, string methodName)
    {
        var type = FindObject(global, typeName);
        if (type is null)
        {
            return null;
        }

        return type.Members.TryGetValue(methodName, out var member)
            ? member as ObjectMethodSymbol
            : null;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }

    private static void AssertNamedTypesBoundTo(IBase2Ast? root, string name, IBaseSymbol parameter)
    {
        var uses = NamedTypes(root)
            .Where(named => string.Equals(SimpleName(named), name, StringComparison.Ordinal))
            .ToList();
        uses.Should().NotBeEmpty($"expected at least one type use of {name}");
        uses.Should().OnlyContain(named => ReferenceEquals(named.BoundSymbol, parameter));
    }

    private static IEnumerable<PhpNamedTypeAst> NamedTypes(IBase2Ast? node)
    {
        if (node is null)
        {
            yield break;
        }

        if (node is PhpNamedTypeAst named)
        {
            yield return named;
        }

        foreach (var child in node.AstChildren)
        {
            foreach (var inner in NamedTypes(child))
            {
                yield return inner;
            }
        }

        foreach (var addon in node.AstGrammarAddons.Values)
        {
            foreach (var inner in NamedTypes(addon))
            {
                yield return inner;
            }
        }
    }

    private static string? SimpleName(PhpNamedTypeAst named)
    {
        var name = named.Name;
        if (name is null)
        {
            return null;
        }

        if (name is TyhpGenericIdentifierAst generic)
        {
            return FirstNonEmpty(generic.Identifier, generic.ValueString);
        }

        return FirstNonEmpty(name.Identifier, name.ValueString)?.TrimStart('\\');
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrEmpty(primary))
        {
            return primary;
        }

        return string.IsNullOrEmpty(fallback) ? null : fallback;
    }
}
