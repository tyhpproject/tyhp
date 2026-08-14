using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parse + AST coverage for PHP 8.5 attributes on top-level (compile-time non-class)
/// <c>const</c> — php-src <c>attributed_top_statement</c> + single-declarator rule.
/// Binder / TARGET_CONSTANT / emit are out of scope here.
/// </summary>
[Trait("Category", "Parser")]
public class ConstAttributesParseTests
{
    [Fact]
    public void Parse_AttributedTopLevelConst_AttachesAttributesToConstList()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class ConstMarker {}

            #[ConstMarker]
            const EXAMPLE = 1;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var constList = FindTopLevelConstLists(result.Ast!).Should().ContainSingle().Subject;
        constList.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "EXAMPLE");
        constList.AstAttributes.Should().HaveCount(1);
        AttributeName(constList.AstAttributes[0]).Should().Be("ConstMarker");
    }

    [Fact]
    public void Parse_AttributedTopLevelConst_WithAttributeArguments_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class ConstMarker {
                public function __construct(public string $label = '') {}
            }

            #[ConstMarker('legacy')]
            const LEGACY = 'x';
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var constList = FindTopLevelConstLists(result.Ast!).Should().ContainSingle().Subject;
        constList.AstAttributes.Should().HaveCount(1);
        AttributeName(constList.AstAttributes[0]).Should().Be("ConstMarker");
    }

    [Fact]
    public void Parse_BareMultiConst_WithoutAttributes_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            const A = 1, B = 2;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var constList = FindTopLevelConstLists(result.Ast!).Should().ContainSingle().Subject;
        constList.GetAllNotNull().Select(c => c.Identifier).Should().BeEquivalentTo(["A", "B"]);
        constList.AstAttributes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AttributedMultiConst_IsRejected()
    {
        // PHP: "Cannot apply attributes to multiple constants at once"
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            #[\Attribute]
            class ConstMarker {}

            #[ConstMarker]
            const A = 1, B = 2;
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "attributed multi-declarator `const` must not parse — PHP requires one declarator when attributed");
    }

    [Fact]
    public void Parse_AttributedConstInsideNamespace_AttachesAttributes()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;

            #[\Attribute]
            class ConstMarker {}

            #[ConstMarker]
            const NS_CONST = 42;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var constList = FindTopLevelConstLists(result.Ast!).Should().ContainSingle().Subject;
        constList.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "NS_CONST");
        constList.AstAttributes.Should().HaveCount(1);
        AttributeName(constList.AstAttributes[0]).Should().Be("ConstMarker");
    }

    private static string? AttributeName(IBase2Ast attribute)
        => (attribute as PhpAttributeAst)?.Name is PhpNameAst name ? name.ValueString : null;

    private static List<PhpConstDeclListAst> FindTopLevelConstLists(IBase2Ast root)
    {
        var lists = new List<PhpConstDeclListAst>();
        Collect(root, lists, insideClass: false);
        return lists;
    }

    private static void Collect(IBase2Ast? node, List<PhpConstDeclListAst> lists, bool insideClass)
    {
        if (node == null)
        {
            return;
        }

        if (node is PhpObjectTypeDeclAst)
        {
            insideClass = true;
        }

        if (node is PhpConstDeclListAst constList && !insideClass)
        {
            lists.Add(constList);
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, lists, insideClass);
        }
    }
}
