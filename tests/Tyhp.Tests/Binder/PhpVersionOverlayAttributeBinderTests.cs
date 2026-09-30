using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// <c>#[\Tyhp\Php]</c> on an overlay declaration must skip-before-bind the same way
/// an inactive <c>declare(php=…)</c> block does: no stamp (8021), no partial-target
/// warning (8031), and no last-wins evict of a still-visible Layer 1 symbol.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20.5")]
public class PhpVersionOverlayAttributeBinderTests
{
    [Fact]
    public void OverlayReplace_AttributeUnsatisfied_OmitsWithout8021()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/array_find.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            function array_find_gated(array $array, callable $callback): mixed;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/array_find.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            // @overlay-against: function array_find_gated(array $array, callable $callback): mixed
            function array_find_gated<TKey, TValue>(
                array<TKey, TValue> $array,
                callable(TValue, TKey): bool $callback
            ): ?TValue;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        FindFunction(global, "array_find_gated").Should().BeNull();
    }

    [Fact]
    public void OverlayReplace_AttributeSatisfied_BindsReplacement()
    {
        using var builder = OverlayFixture(phpVersion: "8.4");
        builder.WithTyhpFile("pkg/_tyhpdef/array_find.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            function array_find_gated(array $array, callable $callback): mixed;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/array_find.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            // @overlay-against: function array_find_gated(array $array, callable $callback): mixed
            function array_find_gated<TKey, TValue>(
                array<TKey, TValue> $array,
                callable(TValue, TKey): bool $callback
            ): ?TValue;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var fn = FindFunction(global, "array_find_gated");
        fn.Should().NotBeNull();
        fn!.GenericParameters.Should().NotBeEmpty();
    }

    [Fact]
    public void OverlayReplace_AttributeUnsatisfied_DoesNotEvictUngatedLayer1()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/always.tyhpdef", """
            <?tyhpdef
            function always_here(): int;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/always.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            // @overlay-against: function always_here(): int
            function always_here(): string;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var fn = FindFunction(global, "always_here");
        fn.Should().NotBeNull();
        TypeText(fn!.ReturnType).Should().Contain("int");
    }

    [Fact]
    public void OverlayPartialFunction_AttributeUnsatisfied_OmitsWithout8031()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/array_find.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            function array_find_gated(array $array, callable $callback): mixed;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/array_find_pure.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            #[\Tyhp\Optimize\Pure]
            // @overlay-against: function array_find_gated(array $array, callable $callback): mixed
            partial function array_find_gated;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefPartialFunctionTargetNotFound);
        FindFunction(global, "array_find_gated").Should().BeNull();
    }

    [Fact]
    public void OverlayPartialFunction_AttributeSatisfied_MergesAttributes()
    {
        using var builder = OverlayFixture(phpVersion: "8.4");
        builder.WithTyhpFile("pkg/_tyhpdef/array_find.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            function array_find_gated(array $array, callable $callback): mixed;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/array_find_pure.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            #[\Tyhp\Optimize\Pure]
            // @overlay-against: function array_find_gated(array $array, callable $callback): mixed
            partial function array_find_gated;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefPartialFunctionTargetNotFound);
        var fn = FindFunction(global, "array_find_gated");
        fn.Should().NotBeNull();
        AttributeNames(fn!.DeclaringAstNode).Should().Contain("Tyhp\\Optimize\\Pure");
    }

    [Fact]
    public void OverlayPartialMember_AttributeUnsatisfied_OmitsWithout8021()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/host.tyhpdef", """
            <?tyhpdef
            class Host {
                #[\Tyhp\Php(">=8.4")]
                public function only_84(): mixed;
                public function always(): int;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/host.tyhpdef", """
            <?tyhpdef
            partial class Host {
                #[\Tyhp\Php(">=8.4")]
                // @overlay-against: function only_84(): mixed
                public function only_84(): string;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var type = FindObject(global, "Host");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("always");
        type.Members.Should().NotContainKey("only_84");
    }

    [Fact]
    public void OverlayPartialMember_AttributeSatisfied_BindsReplacement()
    {
        using var builder = OverlayFixture(phpVersion: "8.4");
        builder.WithTyhpFile("pkg/_tyhpdef/host.tyhpdef", """
            <?tyhpdef
            class Host {
                #[\Tyhp\Php(">=8.4")]
                public function only_84(): mixed;
                public function always(): int;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/host.tyhpdef", """
            <?tyhpdef
            partial class Host {
                #[\Tyhp\Php(">=8.4")]
                // @overlay-against: function only_84(): mixed
                public function only_84(): string;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var type = FindObject(global, "Host");
        type.Should().NotBeNull();
        type!.Members.Should().ContainKey("always");
        type.Members.Should().ContainKey("only_84");
        TypeText(type.Members["only_84"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject.ReturnType)
            .Should().Contain("string");
    }

    [Fact]
    public void OverlayPartialType_TargetGatedOff_OmitsWithout8019()
    {
        using var builder = OverlayFixture(phpVersion: "8.2");
        builder.WithTyhpFile("pkg/_tyhpdef/html.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            class HTMLCollection {
                public function getIterator(): \Iterator;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/html.tyhpdef", """
            <?tyhpdef
            // @overlay-against: class HTMLCollection
            partial class HTMLCollection {
                // @overlay-against: function getIterator(): \Iterator
                public function getIterator(): \Iterator<int, mixed>;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(
            d => d.Code == MessageCode.TyhpdefOverlayPartialTargetNotFound);
        FindObject(global, "HTMLCollection").Should().BeNull();
    }

    private static TestProjectBuilder OverlayFixture(string phpVersion = "8.2")
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson($$"""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "{{phpVersion}}" }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"],
                "overlay": [
                    "./_tyhpdef/overlays/stubs/*.tyhpdef",
                    "./_tyhpdef/overlays/*.tyhpdef"
                ]
            }
            """);
        return builder;
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(TestProjectBuilder builder)
    {
        var project = builder.BuildProject();
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.EnableAstCache = false;
            o.SkipChecking = true;
        });
        var result = compilationService.ParseFiles([userFile], options);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol func
                && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = func;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && !obj.IsExtension
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
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

    private static string TypeText(Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast? node)
    {
        if (node == null)
        {
            return "";
        }

        if (!string.IsNullOrEmpty(node.ValueString) && node is not Tyhp.TyhpLang.Ast.PhpTypeExpressionAst)
        {
            return node.ValueString;
        }

        if (!string.IsNullOrEmpty(node.Identifier))
        {
            return node.Identifier;
        }

        foreach (var child in node.AstChildren)
        {
            var nestedText = TypeText(child);
            if (!string.IsNullOrEmpty(nestedText))
            {
                return nestedText;
            }
        }

        return "";
    }

    private static IReadOnlyList<string> AttributeNames(Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast? node)
    {
        if (node == null)
        {
            return [];
        }

        return [.. node.AstAttributes
            .OfType<Tyhp.TyhpLang.Ast.PhpAttributeAst>()
            .Select(a => a.Name is Tyhp.TyhpLang.Ast.PhpNameAst name
                ? name.ValueString?.TrimStart('\\') ?? ""
                : a.Name?.Identifier?.TrimStart('\\') ?? "")
            .Where(s => !string.IsNullOrEmpty(s))];
    }
}
