using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Story20.7")]
public class PropertyHookBinderTests
{
    [Fact]
    public void Bind_TyhpdefGetSetHooks_SetsHookFlags()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhpdef
            class Holder {
                public string $name { get; set; }
            }
            """, fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d => d.Code.ToString().Contains("VisitorUnexpectedAlternative"),
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "name");
        prop.HasAccessor.Should().BeTrue();
        prop.HasGetHook.Should().BeTrue();
        prop.HasSetHook.Should().BeTrue();
        prop.GetHookReturnsRef.Should().BeFalse();
        prop.AccessorKind.Should().BeNull();
    }

    [Fact]
    public void Bind_TyhpdefByRefGetHook_SetsGetHookReturnsRef()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhpdef
            class Holder {
                public array $items { &get; }
            }
            """, fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d => d.Code.ToString().Contains("VisitorUnexpectedAlternative"),
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "items");
        prop.HasAccessor.Should().BeTrue();
        prop.HasGetHook.Should().BeTrue();
        prop.HasSetHook.Should().BeFalse();
        prop.GetHookReturnsRef.Should().BeTrue();
        prop.AccessorKind.Should().Be(AccessorType.Get);
    }

    [Fact]
    public void Bind_TyhpSourceHookedProperty_SetsSameFlags()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            interface HasName {
                public string $name { get; set; }
            }

            class Temperature {
                public float $fahrenheit {
                    get => 32.0;
                    set(float $value) {}
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var iface = FindProperty(FindObject(global!, "HasName"), "name");
        iface.HasAccessor.Should().BeTrue();
        iface.HasGetHook.Should().BeTrue();
        iface.HasSetHook.Should().BeTrue();
        iface.GetHookReturnsRef.Should().BeFalse();

        var cls = FindProperty(FindObject(global!, "Temperature"), "fahrenheit");
        cls.HasAccessor.Should().BeTrue();
        cls.HasGetHook.Should().BeTrue();
        cls.HasSetHook.Should().BeTrue();
        cls.GetHookReturnsRef.Should().BeFalse();
    }

    [Fact]
    public void Bind_TyhpSourceByRefGetHook_SetsGetHookReturnsRef()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            interface HasItems {
                public array $items { &get; }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "HasItems"), "items");
        prop.HasAccessor.Should().BeTrue();
        prop.HasGetHook.Should().BeTrue();
        prop.HasSetHook.Should().BeFalse();
        prop.GetHookReturnsRef.Should().BeTrue();
    }

    [Fact]
    public void Bind_TyhpdefGetOnlyHook_SetsAccessorKindGet()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhpdef
            class Holder {
                public int $count { get; }
            }
            """, fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d => d.Code.ToString().Contains("VisitorUnexpectedAlternative"),
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "count");
        prop.HasAccessor.Should().BeTrue();
        prop.HasGetHook.Should().BeTrue();
        prop.HasSetHook.Should().BeFalse();
        prop.GetHookReturnsRef.Should().BeFalse();
        prop.AccessorKind.Should().Be(AccessorType.Get);
    }

    [Fact]
    public void Bind_TyhpdefSetOnlyHook_SetsAccessorKindSet()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhpdef
            class Holder {
                public string $name { set; }
            }
            """, fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d => d.Code.ToString().Contains("VisitorUnexpectedAlternative"),
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "name");
        prop.HasAccessor.Should().BeTrue();
        prop.HasGetHook.Should().BeFalse();
        prop.HasSetHook.Should().BeTrue();
        prop.GetHookReturnsRef.Should().BeFalse();
        prop.AccessorKind.Should().Be(AccessorType.Set);
    }

    [Fact]
    public void Bind_TyhpdefInvalidHookName_DoesNotSetGetOrSetFlags()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhpdef
            class Holder {
                public string $name { lazy; }
            }
            """, fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d => d.Code.ToString().Contains("VisitorUnexpectedAlternative"),
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "name");
        prop.HasAccessor.Should().BeTrue("a hook list is present even though the hook name is invalid");
        prop.HasGetHook.Should().BeFalse();
        prop.HasSetHook.Should().BeFalse();
        prop.GetHookReturnsRef.Should().BeFalse();
        prop.AccessorKind.Should().BeNull();
    }

    [Fact]
    public void Bind_PromotedCtorPropertyWithHooks_SetsSameFlags()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class Holder {
                public function __construct(public string $name { get; set; }) {}
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "name");
        prop.HasAccessor.Should().BeTrue();
        prop.HasGetHook.Should().BeTrue();
        prop.HasSetHook.Should().BeTrue();
        prop.GetHookReturnsRef.Should().BeFalse();
    }

    [Fact]
    public void Bind_UnhookedTyhpdefProperty_HasAccessorFalse()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhpdef
            class Holder {
                public int $a;
            }
            """, fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(d => d.Code.ToString().Contains("VisitorUnexpectedAlternative"),
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(FindObject(global!, "Holder"), "a");
        prop.HasAccessor.Should().BeFalse();
        prop.HasGetHook.Should().BeFalse();
        prop.HasSetHook.Should().BeFalse();
        prop.GetHookReturnsRef.Should().BeFalse();
        prop.AccessorKind.Should().BeNull();
    }

    private static ObjectDeclarationSymbol FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null) return;
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol obj
                    && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = obj;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        found.Should().NotBeNull($"expected to find class '{name}'");
        return found!;
    }

    private static ObjectPropertySymbol FindProperty(ObjectDeclarationSymbol type, string name)
    {
        foreach (var member in type.EnumerateMembersAndConstants())
        {
            if (member is ObjectPropertySymbol property
                && string.Equals(
                    property.Name.TrimStart('$'),
                    name.TrimStart('$'),
                    StringComparison.Ordinal))
            {
                return property;
            }
        }

        throw new InvalidOperationException($"Property '{name}' was not bound on {type.Name}.");
    }
}
