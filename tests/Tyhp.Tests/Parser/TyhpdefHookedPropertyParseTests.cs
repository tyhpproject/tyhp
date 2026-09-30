using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Grammar accept/reject and visitor mapping for bodyless tyhpdef hooked properties
/// (<c>#tyhpdefClassPropertyAccessors</c> → <see cref="PhpPropertyAst.Hooks"/>).
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefHookedPropertyParseTests
{
    [Theory]
    [InlineData("public string $name { get; set; }")]
    [InlineData("public array $items { &get; set; }")]
    [InlineData("public string $name { get; private set; }")]
    [InlineData("public string $name { final get; set; }")]
    [InlineData("public string $name { #[Attr] get; set; }")]
    [InlineData("public string $name { get; }")]
    [InlineData("public array $items { &get; }")]
    [InlineData("public string $name { #[Attr] final get; private set; }")]
    public void Parse_TyhpdefHookedProperty_Succeeds(string member)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(
            "<?tyhpdef\nclass Holder {\n    " + member + "\n}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_TyhpdefHookedProperty_WithPropertyLevelAttribute_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                #[Attr]
                public string $name { get; set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_TyhpdefHookedProperty_OnInterfaceAndTrait_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            interface HasName {
                public string $name { get; set; }
            }
            trait Named {
                public string $name { get; private set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_UnhookedCommaPropertyList_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public int $a, $b;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
    }

    [Theory]
    [InlineData("public string $name { get { return $this->x; } }")]
    [InlineData("public string $name { get => $this->x; }")]
    [InlineData("public int $a, $b { get; }")]
    [InlineData("public string $name { }")]
    [InlineData("public string $name { set(string $value); }")]
    public void Parse_TyhpdefHookedProperty_InvalidShape_Fails(string member)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(
            "<?tyhpdef\nclass Holder {\n    " + member + "\n}\n");

        result.Diagnostics.HasErrors.Should().BeTrue(
            "bodyful hooks, comma+hooks, set-parameter lists, and empty hook lists must not parse in tyhpdef");
    }

    [Fact]
    public void Parse_TyhpPropertyHookBodies_Unchanged()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Temperature {
                private float $celsius = 0.0;
                public float $fahrenheit = 32.0 {
                    get => ($this->celsius * 9 / 5) + 32;
                    set(float $value) {
                        $this->celsius = ($value - 32) * 5 / 9;
                    }
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_TyhpInterfaceSemicolonHooks_Unchanged()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            interface HasName {
                public string $name { get; set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_TyhpdefStructProperty_DoesNotAcceptHooks()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            type Point = struct {
                int $x { get; set; }
            };
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "struct properties have no hook list");
    }

    [Fact]
    public void Visit_TyhpdefGetSetHooks_MapsBodylessHookList()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public string $name { get; set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(result.Ast!, "name");
        prop.Should().NotBeNull();
        prop!.Hooks.Should().NotBeNull();
        var hooks = prop.Hooks!.GetAllNotNull().ToList();
        hooks.Should().HaveCount(2);
        hooks.Should().OnlyContain(h => h.Body == null);
        hooks.Should().OnlyContain(h => !h.ReturnsRef);
        hooks.Select(h => h.Identifier).Should().BeEquivalentTo(["get", "set"]);
    }

    [Fact]
    public void Visit_TyhpdefByRefGetHook_SetsReturnsRef()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public array $items { &get; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var get = FindProperty(result.Ast!, "items")!.Hooks!.GetAllNotNull().Single();
        get.Identifier.Should().Be("get");
        get.ReturnsRef.Should().BeTrue();
        get.Body.Should().BeNull();
    }

    [Fact]
    public void Visit_TyhpdefHookModifiersAndAttributes_AttachToHooks()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public string $name { #[Attr] final get; private set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var hooks = FindProperty(result.Ast!, "name")!.Hooks!.GetAllNotNull().ToList();
        var get = hooks.Single(h => h.Identifier == "get");
        var set = hooks.Single(h => h.Identifier == "set");

        get.AstAttributes.Should().HaveCount(1);
        get.Modifiers.Should().NotBeNull();
        get.Modifiers!.Modifiers.Should().Contain(PhpModifier.Final);
        set.Modifiers.Should().NotBeNull();
        set.Modifiers!.Modifiers.Should().Contain(PhpModifier.Private);
        get.Body.Should().BeNull();
        set.Body.Should().BeNull();
    }

    [Fact]
    public void Visit_UnhookedTyhpdefProperty_LeavesHooksNull()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public int $a;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var prop = FindProperty(result.Ast!, "a");
        prop.Should().NotBeNull();
        prop!.Hooks.Should().BeNull();
    }

    private static PhpPropertyAst? FindProperty(IBase2Ast root, string name)
    {
        PhpPropertyAst? found = null;
        void Walk(IBase2Ast? node)
        {
            if (found != null || node == null)
            {
                return;
            }

            if (node is PhpPropertyAst prop
                && string.Equals(
                    prop.Identifier.TrimStart('$'),
                    name.TrimStart('$'),
                    StringComparison.Ordinal))
            {
                found = prop;
                return;
            }

            foreach (var child in node.AstChildren)
            {
                Walk(child);
            }
        }

        Walk(root);
        return found;
    }
}
