using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class EdgeCaseParseTests
{
    [Fact]
    public void Parse_EmptyTyhpOpenTag_ReturnsMinimalAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n");
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_WhitespaceAndCommentsOnly_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n// comment only\n");
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_NamespaceOnlyBody_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\nnamespace App;\n");
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_DeeplyNestedBlocks_DoesNotOverflow()
    {
        var body = string.Concat(Enumerable.Repeat("{ ", 12)) + "$x = 1;" + string.Concat(Enumerable.Repeat(" }", 12));
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\nfunction nested(): void\n{body}\n");
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_LongLine_DoesNotTruncate()
    {
        var longLiteral = new string('a', 10_000);
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n$x = '{longLiteral}';\n");
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_AllSupportedLiterals_Succeeds()
    {
        var source = """
            <?tyhp
            $ints = 42;
            $floats = 3.14;
            $strings = 'hello';
            $bools = true;
            $nulls = null;
            """;
        var result = ParserTestHelper.ParseTyhpContent(source);
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }
}
