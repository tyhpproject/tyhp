using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Tyhp.Domain.Diagnostics;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefPartialTypeHeaderParseTests
{
    [Theory]
    [InlineData("partial class Foo;")]
    [InlineData("partial class \\Foo;")]
    [InlineData("partial class Foo<T>;")]
    [InlineData("partial class Foo<TObject extends object = object, TData = mixed>;")]
    [InlineData("partial class Foo implements \\Countable;")]
    [InlineData("partial class Foo extends Bar;")]
    [InlineData("partial class Foo as Bar;")]
    [InlineData("partial class SplObjectStorage as ObjectStorage;")]
    [InlineData("#[\\Tyhp\\Optimize\\Pure] partial class Foo;")]
    [InlineData("partial interface Iterator<TValue>;")]
    [InlineData("partial trait Box<T>;")]
    [InlineData("partial enum Suit;")]
    public void Parse_HeaderOnlyPartialType_Succeeds(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().Be(0, $"header-only partial type must parse: {declaration}");
    }

    [Fact]
    public void Parse_ClassWithoutPartial_Semicolon_Fails()
    {
        var tree = ParseTyhpdefSyntax("<?tyhpdef\nclass Foo;\n", out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Visit_HeaderOnlyPartialClass_IsHeaderOnlyWithNullBody()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial class Storage<TData> implements \Countable;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        type.IsPartial.Should().BeTrue();
        type.IsHeaderOnly.Should().BeTrue();
        type.Body.Should().BeNull();
        type.AstAttributes.Should().NotBeEmpty();
        type.Implements.Should().NotBeNull();
    }

    [Fact]
    public void Visit_BracePartialClass_IsNotHeaderOnly()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            partial class Storage {
                public function get(): mixed;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        type.IsPartial.Should().BeTrue();
        type.IsHeaderOnly.Should().BeFalse();
        type.Body.Should().NotBeNull();
    }

    private static IParseTree ParseTyhpdefSyntax(string source, out int syntaxErrors)
    {
        var contentBytes = Encoding.UTF8.GetBytes(source);
        var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
        var lexer = new TyhpdefLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(enabled: false, languageMode: string.Empty, new DiagnosticBag(), fileName: "test.tyhpdef");

        var tokenStream = new CommonTokenStream(lexer);
        var parser = new TyhpdefParser(tokenStream, TextWriter.Null, TextWriter.Null);
        parser.RemoveErrorListeners();

        var tree = parser.tyhpdefSrcFile();
        syntaxErrors = parser.NumberOfSyntaxErrors;
        return tree;
    }

    private static IEnumerable<IBase2Ast> Flatten(IBase2Ast? node)
    {
        if (node == null)
        {
            yield break;
        }

        yield return node;
        foreach (var child in node.AstChildren)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
