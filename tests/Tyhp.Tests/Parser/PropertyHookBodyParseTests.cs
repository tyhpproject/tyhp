using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class PropertyHookBodyParseTests
{
    [Fact]
    public void Parse_InterfacePropertyHooks_WithSemicolonBodies_Succeeds()
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

        var hooks = FindPropertyHooks(result.Ast!);
        hooks.Should().HaveCount(2);
        hooks.Should().OnlyContain(h => h.Body == null);
        hooks.Select(h => h.Identifier).Should().BeEquivalentTo(["get", "set"]);
    }

    [Fact]
    public void Parse_AbstractPropertyHooks_WithSemicolonBodies_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            abstract class Named {
                abstract public string $title { get; set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();

        var hooks = FindPropertyHooks(result.Ast!);
        hooks.Should().HaveCount(2);
        hooks.Should().OnlyContain(h => h.Body == null);
        hooks.Select(h => h.Identifier).Should().BeEquivalentTo(["get", "set"]);
    }

    [Fact]
    public void Parse_PropertyHook_WithBlockAndArrowBodies_StillSucceeds()
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
        hooks.Should().OnlyContain(h => h.Body != null);
        hooks.Single(h => h.Identifier == "get").IsExpressionBody.Should().BeTrue();
        hooks.Single(h => h.Identifier == "set").IsExpressionBody.Should().BeFalse();
    }

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
