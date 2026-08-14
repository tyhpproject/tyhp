using Tyhp.TyhpLang.Ast;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class AstStructureTests
{
    [Fact]
    public void Parse_ClassDeclaration_ProducesClassAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo {
                public int $x = 0;
                public function bar(): void {}
            }
            """);

        result.Success.Should().BeTrue();
        result.Ast.Should().BeOfType<TyhpSrcFileAst>();
        result.Ast!.AstChildren.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Parse_FunctionDeclaration_ProducesFunctionAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function add(int $a, int $b): int {
                return $a + $b;
            }
            """);

        result.Success.Should().BeTrue();
        result.Ast.Should().NotBeNull();
        result.Ast!.AstChildren.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_StructDeclaration_ProducesStructAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            struct Point {
                int $x = 0;
                int $y = 0;
            }
            """);

        result.Success.Should().BeTrue();
        result.Ast!.AstChildren.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_GenericUsage_ProducesAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            /** @var array<int, string> $items */
            $items = [];
            """);

        result.Success.Should().BeTrue();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_TypeAlias_ProducesAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type UserId = int;
            """);

        result.Success.Should().BeTrue();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ImportStatement_ProducesAst()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            use App\Models\User;
            """);

        result.Success.Should().BeTrue();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Parse_OperatorOverload_ProducesAst()
    {
        var result = ParserTestHelper.ParseFile(
            Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/parser/operator_overload.tyhp"));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Parse_ExtensionDeclaration_ProducesAst()
    {
        var result = ParserTestHelper.ParseFile(
            Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/parser/extension_declaration.tyhp"));
        result.Success.Should().BeTrue();
    }
}
