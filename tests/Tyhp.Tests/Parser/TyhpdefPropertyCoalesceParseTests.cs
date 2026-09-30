using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Grammar accept/reject for tyhpdef property expected defaults (<c>?? expr</c>).
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefPropertyCoalesceParseTests
{
    [Fact]
    public void Parse_TyhpdefProperty_WithNullCoalesce_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                protected ?\Acme\Log\LoggerInterface $logger ?? null;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var prop = FindProperty(result.Ast!, "logger");
        prop.Should().NotBeNull();
        prop!.DefaultValue.Should().NotBeNull();
        prop.DefaultValue!.ValueString.Should().Be("null");
    }

    [Fact]
    public void Parse_TyhpdefProperty_WithEmptyArrayCoalesce_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                private array $items ?? [];
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var prop = FindProperty(result.Ast!, "items");
        prop.Should().NotBeNull();
        prop!.DefaultValue.Should().NotBeNull();
        (prop.DefaultValue is PhpArrayAst or PhpArrayPairListAst).Should().BeTrue(
            "?? [] should parse as an array expression");
    }

    [Fact]
    public void Parse_TyhpdefProperty_WithoutCoalesce_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public int $id;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindProperty(result.Ast!, "id")!.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void Parse_TyhpdefProperty_CommaListWithPerItemCoalesce_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public int $a ?? 0, $b;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindProperty(result.Ast!, "a")!.DefaultValue.Should().NotBeNull();
        FindProperty(result.Ast!, "b")!.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void Parse_TyhpdefHookedProperty_WithCoalesceBeforeHooks_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public string $name ?? "x" { get; set; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var prop = FindProperty(result.Ast!, "name");
        prop.Should().NotBeNull();
        prop!.DefaultValue.Should().NotBeNull();
        prop.Hooks.Should().NotBeNull();
        prop.Hooks!.GetAllNotNull().Should().HaveCount(2);
    }

    [Fact]
    public void Parse_TyhpdefProperty_EqualsInitializer_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public int $count = 0;
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "tyhpdef properties must use ??, not =");
    }

    [Fact]
    public void Parse_TyhpdefClassConst_EqualsInitializer_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public const int X = 1;
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "tyhpdef class consts must use ??, not =");
    }

    [Fact]
    public void Parse_TyhpdefGlobalConst_EqualsInitializer_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            const int X = 1;
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "tyhpdef global consts must use ??, not =");
    }

    [Fact]
    public void Parse_TyhpdefClassConst_Coalesce_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                public const int X ?? 1;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Parse_TyhpdefParameterDefault_Equals_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            function foo(int $x = 1): void;
            class Holder {
                public function bar(int $y = 2): void;
                public function __construct(int $count = 0): void;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Parse_TyhpClassConst_Equals_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Holder {
                public const int X = 1;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Parse_TyhpProperty_Equals_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Holder {
                public int $count = 0;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
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
