using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Standalone tyhpdef <c>extends&lt;T&gt; Type { }</c> groups must put those type parameters
/// in scope for the group target and for member signatures.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefNestedExtensionGroupGenericBinderTests
{
    [Fact]
    public void NestedGroups_ScopeTypeParametersOnTargetAndMembers()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            extension Arr {
                extends<TKey extends int|string, TValue> array<TKey, TValue> {
                    fn keys(): array<int, TKey> => $this;
                    fn mapped<TResult>(callable(TValue): TResult $callback): array<TKey, TResult> => $this;
                }
                extends<TThisReturn, TNextCallable extends callable> array<int, callable(__CallableReturnType<TNextCallable>): TThisReturn> {
                    fn compose(array<TNextCallable> $next): TThisReturn => $next;
                }
            }
            """,
            skipChecking: false);

        var unbound = result.Diagnostics.Errors
            .Where(error => error.Code is MessageCode.BinderSymbolNotFound
                or MessageCode.BinderUnresolvedReturnType
                or MessageCode.BinderUnresolvedParameterType
                or MessageCode.ExtensionOperatorTargetNotFound)
            .Where(MentionsOpenTypeParameter)
            .ToList();
        unbound.Should().BeEmpty(string.Join("; ", unbound.Select(Describe)));

        var arr = FindObject(result.GlobalScope!, "Arr");
        arr.Should().NotBeNull();
        arr!.GenericParameters.Should().BeEmpty();

        var groups = FindTargetGroups(arr);
        groups.Should().HaveCount(2);

        var arrayGroup = groups.Single(group =>
            group.GenericParameters.Any(parameter => parameter.Name == "TKey"));
        arrayGroup.GenericParameters.Select(parameter => parameter.Name)
            .Should().Equal("TKey", "TValue");
        AssertParametersBound(arrayGroup, "TKey", "TValue");

        var mapped = arr.Members["mapped"] as ObjectMethodSymbol;
        mapped.Should().NotBeNull();
        mapped!.GenericParameters.Select(parameter => parameter.Name).Should().Equal("TResult");
        AssertNamedTypesBoundTo(
            arrayGroup.DeclaringAstNode,
            "TResult",
            mapped.GenericParameters[0]);

        var closureGroup = groups.Single(group =>
            group.GenericParameters.Any(parameter => parameter.Name == "TThisReturn"));
        closureGroup.GenericParameters.Select(parameter => parameter.Name)
            .Should().Equal("TThisReturn", "TNextCallable");
        AssertParametersBound(closureGroup, "TThisReturn", "TNextCallable");
    }

    /// <summary>
    /// Two sibling <c>extends&lt;T&gt;</c> groups that both happen to name their type
    /// parameter <c>T</c> must not share a <see cref="GenericTypeParameterSymbol"/> — each
    /// group's <c>T</c> is scoped to that group's own subtree only, so a leak would show up
    /// as one group's members binding to the other group's parameter symbol.
    /// </summary>
    [Fact]
    public void SiblingGroups_DoNotShareGenericParameterSymbols()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            extension Arr {
                extends<T> array<int, T> {
                    fn first(): T => $this;
                }
                extends<T> array<string, T> {
                    fn second(): T => $this;
                }
            }
            """,
            skipChecking: false);

        var unbound = result.Diagnostics.Errors
            .Where(error => error.Code is MessageCode.BinderSymbolNotFound
                or MessageCode.BinderUnresolvedReturnType
                or MessageCode.BinderUnresolvedParameterType
                or MessageCode.ExtensionOperatorTargetNotFound)
            .Where(error => error.Message.Contains('T', StringComparison.Ordinal))
            .ToList();
        unbound.Should().BeEmpty(string.Join("; ", unbound.Select(Describe)));

        var arr = FindObject(result.GlobalScope!, "Arr");
        arr.Should().NotBeNull();

        var groups = FindTargetGroups(arr!);
        groups.Should().HaveCount(2);

        var firstGroup = groups.Single(group => group.Members.ContainsKey("first"));
        var secondGroup = groups.Single(group => group.Members.ContainsKey("second"));
        var firstT = firstGroup.GenericParameters.Single(parameter => parameter.Name == "T");
        var secondT = secondGroup.GenericParameters.Single(parameter => parameter.Name == "T");
        firstT.Should().NotBeSameAs(secondT);

        var first = firstGroup.Members["first"] as ObjectMethodSymbol;
        var second = secondGroup.Members["second"] as ObjectMethodSymbol;
        first!.ReturnType!.BoundSymbol.Should().BeSameAs(firstT);
        second!.ReturnType!.BoundSymbol.Should().BeSameAs(secondT);
    }

    /// <summary>
    /// A header-level member (not inside any nested <c>extends&lt;T&gt;</c> group) that
    /// references a bare name matching a sibling group's type parameter must not resolve —
    /// group type parameters are scoped to their own group's subtree, not the whole
    /// extension. Without an actual <c>TKey</c> class in scope, this must be reported
    /// unresolved rather than silently binding to the nested group's <c>TKey</c>.
    /// </summary>
    [Fact]
    public void HeaderLevelMember_DoesNotSeeNestedGroupTypeParameter()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            extension Arr {
                extends<TKey, TValue> array<TKey, TValue> {
                    fn keys(): array<int, TKey> => $this;
                }
                fn identify(): TKey => $this;
            }
            """,
            skipChecking: false);

        var arr = FindObject(result.GlobalScope!, "Arr");
        arr.Should().NotBeNull();

        var identify = arr!.Members["identify"] as ObjectMethodSymbol;
        identify.Should().NotBeNull();
        identify!.ReturnType!.BoundSymbol.Should().BeNull(
            "a header-level member must not see a nested group's type parameter");

        result.Diagnostics.Errors.Should().Contain(error =>
                error.Code == MessageCode.BinderUnresolvedReturnType
                && error.Message.Contains("TKey", StringComparison.Ordinal),
            "the bare TKey on the header-level member should be reported unresolved, not leaked from the nested group");
    }

    private static void AssertParametersBound(ObjectDeclarationSymbol group, params string[] names)
    {
        foreach (var name in names)
        {
            var parameter = group.GenericParameters.Single(candidate => candidate.Name == name);
            AssertNamedTypesBoundTo(group.DeclaringAstNode, name, parameter);
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

    private static bool MentionsOpenTypeParameter(IDiagnostic error)
    {
        var message = error.Message;
        return message.Contains("TKey", StringComparison.Ordinal)
            || message.Contains("TValue", StringComparison.Ordinal)
            || message.Contains("TThisReturn", StringComparison.Ordinal)
            || message.Contains("TNextCallable", StringComparison.Ordinal)
            || message.Contains("TResult", StringComparison.Ordinal);
    }

    private static string Describe(IDiagnostic error) =>
        $"{(int)error.Code} {error.FileName}:{error.Line}: {error.Message}";

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

    private static List<ObjectDeclarationSymbol> FindTargetGroups(ObjectDeclarationSymbol extension)
    {
        var extensionScope = extension.ContainingScope!
            .GetAllChildScopes()
            .First(scope => ReferenceEquals(scope.DeclarationSymbol, extension));
        return extensionScope.GetAllChildScopes()
            .Select(scope => scope.DeclarationSymbol)
            .OfType<ObjectDeclarationSymbol>()
            .Where(symbol => symbol.IsExtensionTargetGroup)
            .ToList();
    }

    private static ObjectDeclarationSymbol? FindObject(IBaseScope scope, string name)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            if (symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.Ordinal))
            {
                return obj;
            }
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            var found = FindObject(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
