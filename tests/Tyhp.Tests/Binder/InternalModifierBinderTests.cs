using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
public class InternalModifierBinderTests
{
    [Fact]
    public void Bind_InternalClass_SetsIsInternal()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            internal class Foo {}
            """);

        diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        var foo = FindObject(global!, "Foo");
        foo.IsInternal.Should().BeTrue();
        (foo.Visibility & MemberModifier.Internal).Should().NotBe(MemberModifier.None);
        SymbolExportVisibility.OmitFromPublicTyhpdef(foo).Should().BeTrue();
    }

    [Fact]
    public void Bind_InternalFunction_SetsIsInternal()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            internal function bar(): void {}
            """);

        diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        var fn = FindSymbol<FunctionDeclarationSymbol>(global!, "bar");
        fn.IsInternal.Should().BeTrue();
        SymbolExportVisibility.OmitFromPublicTyhpdef(fn).Should().BeTrue();
    }

    [Fact]
    public void Bind_InternalClassMember_SetsIsInternal()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class Baz {
                internal function qux(): void {}
            }
            """);

        diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        var baz = FindObject(global!, "Baz");
        baz.IsInternal.Should().BeFalse();
        SymbolExportVisibility.OmitFromPublicTyhpdef(baz).Should().BeFalse();

        var qux = baz.Members.Values.OfType<ObjectMethodSymbol>()
            .Should().ContainSingle(m => m.Name == "qux").Subject;
        qux.IsInternal.Should().BeTrue();
        SymbolExportVisibility.OmitFromPublicTyhpdef(qux).Should().BeTrue();
    }

    private static ObjectDeclarationSymbol FindObject(IBaseScope global, string name)
        => FindSymbol<ObjectDeclarationSymbol>(global, name);

    private static T FindSymbol<T>(IBaseScope global, string name)
        where T : BaseSymbol
    {
        T? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null)
            {
                return;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is T match
                    && string.Equals(match.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = match;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        found.Should().NotBeNull($"expected to find {typeof(T).Name} '{name}'");
        return found!;
    }
}
