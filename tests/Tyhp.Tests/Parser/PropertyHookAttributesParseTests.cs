using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class PropertyHookAttributesParseTests
{
    [Fact]
    public void Parse_PropertyHooks_WithAttributes_AttachesAttributesToHooks()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class HookMarker {}

            class C {
                private string $_name = '';
                public string $name {
                    #[HookMarker]
                    get => $this->_name;
                    #[HookMarker]
                    set(string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();

        var hooks = FindPropertyHooks(result.Ast!);
        hooks.Should().HaveCount(2);

        var getHook = hooks.Single(h => h.Identifier == "get");
        var setHook = hooks.Single(h => h.Identifier == "set");

        getHook.AstAttributes.Should().HaveCount(1);
        setHook.AstAttributes.Should().HaveCount(1);
        AttributeName(getHook.AstAttributes[0]).Should().Be("HookMarker");
        AttributeName(setHook.AstAttributes[0]).Should().Be("HookMarker");
        getHook.Body.Should().NotBeNull();
        setHook.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_InterfacePropertyHooks_WithAttributesAndSemicolonBodies_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class HookMarker {}

            interface I {
                public string $title {
                    #[HookMarker]
                    get;
                    #[HookMarker]
                    set;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var hooks = FindPropertyHooks(result.Ast!);
        hooks.Should().HaveCount(2);
        hooks.Should().OnlyContain(h => h.Body == null);
        hooks.Should().OnlyContain(h => h.AstAttributes.Count == 1);
        hooks.Select(h => h.Identifier).Should().BeEquivalentTo(["get", "set"]);
    }

    [Fact]
    public void Parse_PropertyHooks_WithoutAttributes_StillSucceeds()
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

        var hooks = FindPropertyHooks(result.Ast!);
        hooks.Should().HaveCount(2);
        hooks.Should().OnlyContain(h => h.AstAttributes.Count == 0);
    }

    private static string? AttributeName(IBase2Ast attribute)
        => (attribute as PhpAttributeAst)?.Name is PhpNameAst name ? name.ValueString : null;

    private static List<PhpPropertyHookAst> FindPropertyHooks(IBase2Ast root)
    {
        var hooks = new List<PhpPropertyHookAst>();
        Collect(root, hooks);
        return hooks;
    }

    private static void Collect(IBase2Ast? node, List<PhpPropertyHookAst> hooks)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpPropertyHookAst hook)
        {
            hooks.Add(hook);
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, hooks);
        }
    }
}
