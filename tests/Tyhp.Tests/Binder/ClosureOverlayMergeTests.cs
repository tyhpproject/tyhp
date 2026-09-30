using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Overlay merge must apply a generic Closure / Fiber header and must not duplicate
/// <c>bind</c> / <c>bindTo</c> against Layer 2 harvest. Uses synthetic stubs, not live Ext.Core.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class ClosureOverlayMergeTests
{
    [Fact]
    public void Overlay_ClosureHeader_MergesCallableThisAndScopeGenerics()
    {
        var (global, diagnostics) = BindEmptyUserFile();

        diagnostics.Errors.Should().NotContain(d => IsBindOrBindToDuplicate(d),
            DescribeBindDuplicates(diagnostics));

        var closure = FindObject(global, "Closure");
        closure.Should().NotBeNull();
        closure!.GenericParameters.Select(g => g.Name)
            .Should().Equal("TCallableShape", "TThis", "TScope");

        TypeText(closure.GenericParameters[0].Constraint).Should().Contain("callable");
        closure.GenericParameters[0].HasDefault.Should().BeFalse(
            "TCallableShape has no default so bare Closure stays gradual");

        TypeText(closure.GenericParameters[1].Constraint).Should().Contain("__ClosureThis");
        TypeText(closure.GenericParameters[1].DefaultType).Should().Contain("__ClosureThis");

        TypeText(closure.GenericParameters[2].Constraint).Should().Contain("__ClosureScope");
        TypeText(closure.GenericParameters[2].DefaultType).Should().Contain("__ClosureScope");
    }

    [Fact]
    public void Overlay_ClosureAliases_AreRegistered()
    {
        var (global, _) = BindEmptyUserFile();

        var thisAlias = FindTypeAlias(global, "__ClosureThis");
        thisAlias.Should().NotBeNull();
        thisAlias!.GenericParameters.Should().BeEmpty();
        TypeText(thisAlias.AliasedType).Should().Contain("object");

        var scopeAlias = FindTypeAlias(global, "__ClosureScope");
        scopeAlias.Should().NotBeNull();
        scopeAlias!.GenericParameters.Select(g => g.Name).Should().Equal("TThis");
        TypeText(scopeAlias.GenericParameters[0].Constraint).Should().Contain("__ClosureThis");
        TypeText(scopeAlias.AliasedType).Should().Contain("__SuperType");
        TypeText(scopeAlias.AliasedType).Should().NotContain("static");
    }

    [Fact]
    public void Overlay_BindAndBindTo_AreOverloadsNotDuplicates()
    {
        var (global, diagnostics) = BindEmptyUserFile();

        diagnostics.Errors.Should().NotContain(d => IsBindOrBindToDuplicate(d),
            DescribeBindDuplicates(diagnostics));

        var closure = FindObject(global, "Closure");
        closure.Should().NotBeNull();

        var bind = closure!.Members["bind"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        bind.IsStatic.Should().BeTrue();
        bind.Overloads.Should().HaveCount(1, "explicit scope + 'static'/omitted");
        TypeText(bind.Parameters[2].DeclaredType).Should().Contain("TNewScope");
        TypeText(bind.GenericParameters.First(g => g.Name == "TNewScope").Constraint)
            .Should().Contain("__ClosureScope");
        TypeText(bind.Overloads[0].GenericParameters.First(g => g.Name == "TNewScope").Constraint)
            .Should().Contain("static");

        var bindTo = closure.Members["bindTo"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        bindTo.IsStatic.Should().BeFalse();
        bindTo.Overloads.Should().HaveCount(1);
        TypeText(bindTo.Parameters[1].DeclaredType).Should().Contain("TNewScope");
        TypeText(bindTo.GenericParameters.First(g => g.Name == "TNewScope").Constraint)
            .Should().Contain("__ClosureScope");
        TypeText(bindTo.Overloads[0].GenericParameters.First(g => g.Name == "TNewScope").Constraint)
            .Should().Contain("static");
    }

    [Fact]
    public void Overlay_Call_RequiresNonNullObjectThis()
    {
        var (global, _) = BindEmptyUserFile();
        var closure = FindObject(global, "Closure");
        closure.Should().NotBeNull();

        var call = closure!.Members["call"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        call.Overloads.Should().BeEmpty();
        call.GenericParameters.Select(g => g.Name).Should().Equal("TNewThis");
        TypeText(call.GenericParameters[0].Constraint).Should().Be("object");
        TypeText(call.GenericParameters[0].Constraint).Should().NotContain("__ClosureThis");
        TypeText(call.Parameters[0].DeclaredType).Should().Contain("TNewThis");
    }

    [Fact]
    public void Overlay_FromCallableAndGetCurrent_MatchCanonicalSurface()
    {
        var (global, _) = BindEmptyUserFile(phpVersion: "8.5");
        var closure = FindObject(global, "Closure");
        closure.Should().NotBeNull();

        var fromCallable = closure!.Members["fromCallable"]
            .Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        fromCallable.IsStatic.Should().BeTrue();
        TypeText(fromCallable.ReturnType).Should().Contain("Closure");
        var fromCallableReturn = TypeText(fromCallable.ReturnType);
        fromCallableReturn.Should().Contain("TCallableShape");
        fromCallableReturn.Should().Contain("__CallableThis");
        fromCallableReturn.Should().Contain("__CallableScope");

        var getCurrent = closure.Members["getCurrent"]
            .Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        getCurrent.IsStatic.Should().BeTrue();
        var getCurrentReturn = TypeText(getCurrent.ReturnType);
        getCurrentReturn.Should().Contain("callable");
        getCurrentReturn.Should().Contain("__ClosureThis");
        getCurrentReturn.Should().Contain("__ClosureScope");
    }

    [Fact]
    public void Overlay_FiberHeader_IsResumeAndCallableShape()
    {
        var (global, diagnostics) = BindEmptyUserFile();

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderDuplicateSymbolDeclaration
            && d.Message.Contains("Fiber", StringComparison.OrdinalIgnoreCase));

        var fiber = FindObject(global, "Fiber");
        fiber.Should().NotBeNull();
        fiber!.GenericParameters.Select(g => g.Name)
            .Should().Equal("TResume", "TCallableShape");
        fiber.GenericParameters.Select(g => g.Name)
            .Should().NotContain("TSuspend")
            .And.NotContain("TStart")
            .And.NotContain("TReturn");
        TypeText(fiber.GenericParameters[0].DefaultType).Should().Contain("mixed");
        TypeText(fiber.GenericParameters[1].Constraint).Should().Contain("callable");
        fiber.GenericParameters[1].HasDefault.Should().BeTrue();
        TypeText(fiber.GenericParameters[1].DefaultType).Should().Contain("callable");
        TypeText(fiber.GenericParameters[1].DefaultType).Should().NotContain("void");

        var resume = fiber.Members["resume"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        TypeText(resume.Parameters[0].DeclaredType).Should().Contain("TResume");
        TypeText(resume.ReturnType).Should().Contain("mixed");
        TypeText(resume.ReturnType).Should().NotContain("TSuspend");

        var start = fiber.Members["start"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        TypeText(start.ReturnType).Should().Contain("mixed");
        TypeText(start.ReturnType).Should().NotContain("TSuspend");

        var throwMethod = fiber.Members["throw"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        TypeText(throwMethod.ReturnType).Should().Contain("mixed");
        TypeText(throwMethod.ReturnType).Should().NotContain("TSuspend");

        var getReturn = fiber.Members["getReturn"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        TypeText(getReturn.ReturnType).Should().Contain("__CallableReturnType");
        TypeText(getReturn.ReturnType).Should().NotContain("null");

        var getCurrent = fiber.Members["getCurrent"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        getCurrent.IsStatic.Should().BeTrue();
        var getCurrentType = TypeText(getCurrent.ReturnType);
        getCurrentType.Should().Contain("Fiber");
        getCurrentType.Should().Contain("mixed");
        getCurrentType.Should().Contain("callable");
        getCurrentType.Should().NotContain("void");

        var suspend = fiber.Members["suspend"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        suspend.IsStatic.Should().BeTrue();
        TypeText(suspend.Parameters[0].DeclaredType).Should().Contain("mixed");
        TypeText(suspend.Parameters[0].DeclaredType).Should().NotContain("TResume");
        TypeText(suspend.ReturnType).Should().Contain("mixed");
        TypeText(suspend.ReturnType).Should().NotContain("TResume");
        TypeText(suspend.ReturnType).Should().NotContain("TSuspend");
    }

    private static bool IsBindOrBindToDuplicate(IDiagnostic d) =>
        d.Code == MessageCode.BinderDuplicateSymbolDeclaration
        && (d.Message.Contains("`bind`", StringComparison.OrdinalIgnoreCase)
            || d.Message.Contains("`bindTo`", StringComparison.OrdinalIgnoreCase));

    private static string DescribeBindDuplicates(DiagnosticBag diagnostics) =>
        string.Join("; ", diagnostics.Errors
            .Where(IsBindOrBindToDuplicate)
            .Select(e => $"{e.Code}: {e.Message}"));

    private static (GlobalScope Global, DiagnosticBag Diagnostics) BindEmptyUserFile(
        string phpVersion = "8.5")
    {
        using var builder = IsolatedCompilation.CreateOverlayProject(
            phpVersion: phpVersion,
            includeTyhpdef: SyntheticPhpStubs.ClosureFiberHarvest,
            overlayTyhpdef: SyntheticPhpStubs.ClosureFiberOverlay);
        var result = IsolatedCompilation.BindProject(builder, skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        return (result.GlobalScope!, result.Diagnostics);
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

    private static TypeAliasSymbol? FindTypeAlias(GlobalScope global, string name)
    {
        TypeAliasSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is TypeAliasSymbol alias
                && string.Equals(alias.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = alias;
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

    private static string TypeText(IBase2Ast? node)
    {
        if (node == null)
        {
            return "";
        }

        var parts = new List<string>();
        CollectTypeText(node, parts, []);
        return string.Join(" ", parts);
    }

    private static void CollectTypeText(IBase2Ast node, List<string> parts, HashSet<IBase2Ast> seen)
    {
        if (!seen.Add(node))
        {
            return;
        }

        if (!string.IsNullOrEmpty(node.ValueString) && node is not Tyhp.TyhpLang.Ast.PhpTypeExpressionAst)
        {
            parts.Add(node.ValueString);
        }
        else if (!string.IsNullOrEmpty(node.Identifier))
        {
            parts.Add(node.Identifier);
        }

        foreach (var child in node.AstChildren)
        {
            CollectTypeText(child, parts, seen);
        }

        foreach (var addon in node.AstGrammarAddons.Values)
        {
            CollectTypeText(addon, parts, seen);
        }
    }
}
