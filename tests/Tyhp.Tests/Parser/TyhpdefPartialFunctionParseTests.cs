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
public class TyhpdefPartialFunctionParseTests
{
    [Theory]
    [InlineData("partial function array_count_values;")]
    [InlineData("partial function \\array_count_values;")]
    [InlineData("#[\\Tyhp\\Optimize\\Pure] partial function \\str_contains;")]
    [InlineData("partial function call_user_func as call_user_func_unsafe;")]
    [InlineData("partial function \\strtolower as str2LC;")]
    public void Parse_NameOnlyPartialFunction_Succeeds(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().Be(0, "name-only partial function must parse");
        ContainsRule<TyhpdefParser.TyhpdefPartialFunctionDeclarationStatementContext>(tree).Should().BeTrue();
        ContainsRule<TyhpdefParser.TyhpdefImportFunctionDeclarationStatementContext>(tree).Should().BeFalse();
    }

    [Theory]
    [InlineData("partial function foo(): void;")]
    [InlineData("partial function foo();")]
    [InlineData("partial function foo(int $x): int;")]
    [InlineData("async partial function foo;")]
    [InlineData("omit partial function foo;")]
    [InlineData("partial function foo as bar(): void;")]
    [InlineData("partial function foo<T>;")]
    public void Parse_PartialFunctionWithSignature_Fails(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, $"signature after partial function must not parse: {declaration}");
        ContainsRule<TyhpdefParser.TyhpdefImportFunctionDeclarationStatementContext>(tree)
            .Should().BeFalse("must not recover into a full function replace");
    }

    [Fact]
    public void Visit_NameOnlyPartialFunction_IsPartialWithoutSignature()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            #[\Tyhp\Optimize\Pure]
            partial function \array_map;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.IsPartial.Should().BeTrue();
        fn.IsOmit.Should().BeFalse();
        fn.Parameters.Should().BeNull();
        fn.ReturnType.Should().BeNull();
        fn.AstAttributes.Should().NotBeEmpty();
    }

    [Fact]
    public void Visit_PartialFunctionAlias_AttachesAliasedAs()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            partial function \call_user_func as call_user_func_unsafe;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.IsPartial.Should().BeTrue();
        fn.NameOrAlias!.AstGrammarAddons.Should().ContainKey("aliasedAs");
        fn.NameOrAlias.AstGrammarAddons["aliasedAs"].ValueString.Should().Be("call_user_func_unsafe");
        (fn.NameOrAlias.ValueString ?? fn.NameOrAlias.Identifier).Should().Contain("call_user_func");
    }

    [Fact]
    public void Visit_PartialFunctionMethod_InsidePartialClass_IsPartial()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            partial class \DateTime {
                #[\Tyhp\Optimize\Pure]
                partial function format;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        type.IsPartial.Should().BeTrue();
        var method = Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single();
        method.IsPartial.Should().BeTrue();
        method.Identifier.Should().Be("format");
        method.Parameters.Should().BeNull();
        method.AstAttributes.Should().NotBeEmpty();
    }

    [Fact]
    public void OverlayAgainstComment_AttachesToPartialFunction()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @overlay-against: function array_map
            partial function \array_map;
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.AstGrammarAddons.Should().ContainKey("overlayAgainst");
        fn.AstGrammarAddons["overlayAgainst"].ValueString.Should().Be("function array_map");
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

    private static bool ContainsRule<T>(IParseTree? node) where T : ParserRuleContext
    {
        if (node is T)
        {
            return true;
        }

        if (node == null)
        {
            return false;
        }

        for (var i = 0; i < node.ChildCount; i++)
        {
            if (ContainsRule<T>(node.GetChild(i)))
            {
                return true;
            }
        }

        return false;
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
