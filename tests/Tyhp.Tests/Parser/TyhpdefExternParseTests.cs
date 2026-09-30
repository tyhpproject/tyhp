using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Tyhp.Domain.Diagnostics;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Story 21.1: parse-level and visitor/AST coverage for tyhpdef <c>extern class</c> /
/// <c>extern interface</c> / <c>extern enum</c>. Illegal shapes still assert against the
/// ANTLR parse tree. Legal declarations also go through <see cref="ParserTestHelper"/>
/// so the visitor builds a class-like <see cref="TyhpdefImportObjectDeclAst"/> with
/// <see cref="TyhpdefImportObjectDeclAst.IsExtern"/>.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefExternParseTests
{
    [Theory]
    [InlineData("extern class \\Acme\\Note\\Document;")]
    [InlineData("extern class Acme\\Document;")]
    [InlineData("extern class Document;")]
    [InlineData("extern interface \\Acme\\Log\\LoggerInterface;")]
    [InlineData("extern interface Acme\\Log\\LoggerInterface;")]
    [InlineData("extern enum Backed\\Kind;")]
    [InlineData("extern enum \\Backed\\Kind;")]
    [InlineData("extern enum \\Acme\\Month;")]
    [InlineData("extern enum Kind;")]
    public void Parse_ExternType_Succeeds(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().Be(0, "name-only extern declarations must parse");
        ContainsRule<TyhpdefParser.TyhpdefExternClassDeclarationStatementContext>(tree)
            .Should().Be(declaration.Contains("class ", StringComparison.Ordinal));
        ContainsRule<TyhpdefParser.TyhpdefExternInterfaceDeclarationStatementContext>(tree)
            .Should().Be(declaration.Contains("interface ", StringComparison.Ordinal));
        ContainsRule<TyhpdefParser.TyhpdefExternEnumDeclarationStatementContext>(tree)
            .Should().Be(declaration.Contains("enum ", StringComparison.Ordinal));
        ContainsRule<TyhpdefParser.TyhpdefExternDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportClassDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportInterfaceDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportEnumDeclarationStatementContext>(tree).Should().BeFalse();
    }

    [Theory]
    [InlineData("extern \\Acme\\Note\\Document;")]
    [InlineData("extern Acme\\Document;")]
    [InlineData("extern Document;")]
    public void Parse_BareExternType_Succeeds(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().Be(0, "kind-unspecified extern declarations must parse");
        ContainsRule<TyhpdefParser.TyhpdefExternDeclarationStatementContext>(tree).Should().BeTrue();
        ContainsRule<TyhpdefParser.TyhpdefExternClassDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefExternInterfaceDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefExternEnumDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportClassDeclarationStatementContext>(tree).Should().BeFalse();
    }

    [Fact]
    public void Parse_BareExtern_InsideNamespace_Succeeds()
    {
        var tree = ParseTyhpdefSyntax("""
            <?tyhpdef
            namespace Acme {
                extern Document;
            }
            """, out var syntaxErrors);

        syntaxErrors.Should().Be(0);
        ContainsRule<TyhpdefParser.TyhpdefExternDeclarationStatementContext>(tree).Should().BeTrue();
    }

    [Theory]
    [InlineData("extern function \\widget_add;")]
    [InlineData("extern function widget_add;")]
    [InlineData("extern function Foo\\widget_add;")]
    public void Parse_ExternFunction_Succeeds(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().Be(0, "name-only extern function declarations must parse");
        ContainsRule<TyhpdefParser.TyhpdefExternFunctionDeclarationStatementContext>(tree).Should().BeTrue();
        ContainsRule<TyhpdefParser.TyhpdefExternDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportFunctionDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefPartialFunctionDeclarationStatementContext>(tree).Should().BeFalse();
    }

    [Theory]
    [InlineData("extern const \\WIDGET_ROUND_PLUSINF;")]
    [InlineData("extern const FOO;")]
    [InlineData("extern const Foo\\BAR;")]
    public void Parse_ExternConst_Succeeds(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().Be(0, "name-only extern const declarations must parse");
        ContainsRule<TyhpdefParser.TyhpdefExternConstDeclarationStatementContext>(tree).Should().BeTrue();
        ContainsRule<TyhpdefParser.TyhpdefExternDeclarationStatementContext>(tree).Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportConstStatementContext>(tree).Should().BeFalse();
    }

    [Fact]
    public void Parse_ExternFunction_InsideNamespace_Succeeds()
    {
        var tree = ParseTyhpdefSyntax("""
            <?tyhpdef
            namespace BcMath {
                extern function widget_add;
            }
            """, out var syntaxErrors);

        syntaxErrors.Should().Be(0);
        ContainsRule<TyhpdefParser.TyhpdefExternFunctionDeclarationStatementContext>(tree).Should().BeTrue();
    }

    [Fact]
    public void Parse_ExternFunction_InTyhpMode_Fails()
    {
        ParseTyhpSyntax("""
            <?tyhp
            extern function widget_add;
            """, out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, "extern is not a keyword in .tyhp");
    }

    [Theory]
    [InlineData("extern function \\widget_add(string $a, string $b): string;")]
    [InlineData("extern function \\widget_add(): void;")]
    [InlineData("extern function \\widget_add {}")]
    [InlineData("extern function \\widget_add as add;")]
    [InlineData("extern const int \\FOO;")]
    [InlineData("extern const \\FOO = 1;")]
    [InlineData("extern const \\FOO ?? 0;")]
    [InlineData("deprecated extern function \\widget_add;")]
    [InlineData("omit extern function \\widget_add;")]
    [InlineData("partial extern function \\widget_add;")]
    [InlineData("obsolete extern const \\FOO;")]
    [InlineData("deprecated extern const \\FOO;")]
    [InlineData("partial extern const \\FOO;")]
    [InlineData("omit extern const \\FOO;")]
    public void Parse_ExternFunctionOrConst_IllegalShape_Fails(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, $"illegal form must not parse: {declaration}");
        ContainsRule<TyhpdefParser.TyhpdefImportFunctionDeclarationStatementContext>(tree)
            .Should().BeFalse("must not recover into a signed function");
        ContainsRule<TyhpdefParser.TyhpdefImportConstStatementContext>(tree)
            .Should().BeFalse("must not recover into a typed const");
    }

    [Fact]
    public void Visit_ExternFunction_FullyQualified_IsExtern()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-math
            extern function \widget_add;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var function = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        function.IsExtern.Should().BeTrue();
        function.Identifier.Should().Be(@"\widget_add");
        function.Parameters.Should().BeNull();
        function.ReturnType.Should().BeNull();
        function.IsPartial.Should().BeFalse();
        function.IsOmit.Should().BeFalse();
        function.ProvidedBy.Should().Be("tyhpdef/acme-math");
    }

    [Fact]
    public void Visit_ExternConst_FullyQualified_IsExtern()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-int
            extern const \WIDGET_ROUND_PLUSINF;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var constant = Flatten(result.Ast).OfType<TyhpdefImportConstAst>().Single();
        constant.IsExtern.Should().BeTrue();
        constant.Identifier.Should().Be(@"\WIDGET_ROUND_PLUSINF");
        constant.TypeExpr.Should().BeNull();
        constant.CoalesceExpr.Should().BeNull();
        constant.ProvidedBy.Should().Be("tyhpdef/acme-int");
    }

    [Fact]
    public void Parse_ExternEnum_FullyQualified_ViaNamespace_Succeeds()
    {
        // Leading-backslash `extern enum \Backed\Kind;` is covered by Parse_ExternType_Succeeds.
        // This checks the namespace wrapper still parses.
        var tree = ParseTyhpdefSyntax("""
            <?tyhpdef
            namespace Backed {
                extern enum Kind;
            }
            """, out var syntaxErrors);

        syntaxErrors.Should().Be(0);
        ContainsRule<TyhpdefParser.TyhpdefExternEnumDeclarationStatementContext>(tree).Should().BeTrue();
    }

    [Fact]
    public void Parse_ExternClass_InsideNamespace_Succeeds()
    {
        var tree = ParseTyhpdefSyntax("""
            <?tyhpdef
            namespace Acme {
                extern class Document;
            }
            """, out var syntaxErrors);

        syntaxErrors.Should().Be(0);
        ContainsRule<TyhpdefParser.TyhpdefExternClassDeclarationStatementContext>(tree).Should().BeTrue();
    }

    [Fact]
    public void Parse_MethodNamedExtern_ParsesAsIdentifier_NotKeyword()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Worker {
                public function extern(): void;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single().Identifier.Should().Be("extern");
    }

    [Fact]
    public void Parse_FunctionNamedExtern_ParsesAsIdentifier_NotKeyword()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            function \extern(mixed $value): void;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("extern class Foo { }")]
    [InlineData("extern class Foo { public function bar(): void; }")]
    [InlineData("extern class Foo extends Bar;")]
    [InlineData("extern class Foo implements Bar;")]
    [InlineData("extern class Foo as Bar;")]
    [InlineData("extern class Foo<T>;")]
    [InlineData("extern enum Foo: int;")]
    [InlineData("extern enum Foo implements Bar;")]
    [InlineData("deprecated extern class Foo;")]
    [InlineData("omit extern class Foo;")]
    [InlineData("partial extern class Foo;")]
    public void Parse_ExternKeywordForm_IllegalShape_FailsWithoutClassBody(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, $"illegal form must not parse: {declaration}");
        ContainsRule<TyhpdefParser.TyhpdefImportClassDeclarationStatementContext>(tree)
            .Should().BeFalse("must not recover into a class-with-body");
        ContainsRule<TyhpdefParser.TyhpdefImportInterfaceDeclarationStatementContext>(tree)
            .Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportEnumDeclarationStatementContext>(tree)
            .Should().BeFalse();
        ContainsRule<TyhpdefParser.TyhpdefImportTraitDeclarationStatementContext>(tree)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("extern abstract class Foo;")]
    [InlineData("extern partial class Foo;")]
    [InlineData("extern trait Foo;")]
    [InlineData("extern trait Foo { }")]
    public void Parse_ExternKeyword_IllegalFollower_Fails(string declaration)
    {
        // `extern` is T_TYHPDEF_EXTERN when the next token looks like a name or
        // class/interface/enum, so `extern trait` / `extern abstract` commit to
        // the extern keyword and still fail to parse.
        ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, $"illegal form must not parse: {declaration}");
    }

    [Theory]
    [InlineData("extern Foo { }")]
    [InlineData("extern Foo extends Bar;")]
    public void Parse_BareExtern_IllegalShape_Fails(string declaration)
    {
        var tree = ParseTyhpdefSyntax($"<?tyhpdef\n{declaration}\n", out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, $"illegal form must not parse: {declaration}");
        ContainsRule<TyhpdefParser.TyhpdefImportClassDeclarationStatementContext>(tree)
            .Should().BeFalse("must not recover into a class-with-body");
    }

    [Fact]
    public void Parse_ExternClass_InTyhpMode_Fails()
    {
        var tree = ParseTyhpSyntax("""
            <?tyhp
            extern class Foo {}
            """, out var syntaxErrors);

        syntaxErrors.Should().BeGreaterThan(0, "extern is not a keyword in .tyhp");
    }

    [Fact]
    public void Parse_TyhpMethodNamedExtern_ParsesAsIdentifier()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Worker {
                public function extern(): void {}
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single().Identifier.Should().Be("extern");
    }

    [Fact]
    public void Visit_ExternClass_FullyQualified_IsExternEmptyMembers()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extern class \Foo\Bar;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "class", @"\Foo\Bar", providedBy: null);
    }

    [Fact]
    public void Visit_ExternInterface_FullyQualified_IsExternEmptyMembers()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extern interface \Acme\Log\LoggerInterface;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "interface", @"\Acme\Log\LoggerInterface", providedBy: null);
    }

    [Fact]
    public void Visit_ExternEnum_FullyQualified_IsExternEmptyMembers()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extern enum \Acme\Month;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "enum", @"\Acme\Month", providedBy: null);
    }

    [Fact]
    public void Visit_ExternEnum_Namespaced_IsExternEmptyMembers()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            namespace Backed {
                extern enum Kind;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "enum", "Kind", providedBy: null);
    }

    [Fact]
    public void Visit_BareExtern_FullyQualified_IsExternKindUnspecified()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extern \Foo\Bar;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "extern", @"\Foo\Bar", providedBy: null);
    }

    [Fact]
    public void Visit_BareExtern_ProvidedByComment_AttachesPackageName()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-search
            extern \Acme\Note\Document;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "extern", @"\Acme\Note\Document", "tyhpdef/acme-search");
    }

    [Fact]
    public void Visit_ExternClass_ProvidedByComment_AttachesPackageName()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-search
            extern class \Acme\Note\Document;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        AssertExternPlaceholder(type, "class", @"\Acme\Note\Document", "tyhpdef/acme-search");
    }

    [Fact]
    public void Visit_ExternClass_ProvidedByComment_TrimsWhitespace()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @provided-by:   tyhpdef/acme-curl
            extern class \CurlHandle;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single()
            .ProvidedBy.Should().Be("tyhpdef/acme-curl");
    }

    [Fact]
    public void Visit_ExternClass_UnknownAtTag_DoesNotSetProvidedBy()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @not-a-real-tag: tyhp/ignored
            extern class \Foo\Bar;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single()
            .ProvidedBy.Should().BeNull();
    }

    [Fact]
    public void Visit_ExternClass_UnknownAtTagThenProvidedBy_UsesNearestProvidedBy()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @unknown: ignored
            // @provided-by: tyhpdef/acme-search
            extern class \Acme\Note\Client;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single()
            .ProvidedBy.Should().Be("tyhpdef/acme-search");
    }

    [Fact]
    public void Visit_ExternClass_AbsentProvidedBy_LeavesNull()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extern class \Acme\Note\Message;
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single()
            .ProvidedBy.Should().BeNull();
    }

    [Fact]
    public void Visit_RegularClass_IsNotExtern()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Real {
            }
            extern class \Foo\Bar;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var types = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().ToList();
        types.Should().HaveCount(2);
        types.Single(t => t.Identifier == "Real").IsExtern.Should().BeFalse();
        types.Single(t => t.Identifier == @"\Foo\Bar").IsExtern.Should().BeTrue();
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

    private static IParseTree ParseTyhpSyntax(string source, out int syntaxErrors)
    {
        var contentBytes = Encoding.UTF8.GetBytes(source);
        var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
        var lexer = new TyhpLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(enabled: false, languageMode: string.Empty, new DiagnosticBag(), fileName: "test.tyhp");

        var tokenStream = new CommonTokenStream(lexer);
        var parser = new TyhpParser(tokenStream, TextWriter.Null, TextWriter.Null);
        parser.RemoveErrorListeners();

        var tree = parser.tyhpSrcFile();
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

    private static void AssertExternPlaceholder(
        TyhpdefImportObjectDeclAst type,
        string declKind,
        string identifier,
        string? providedBy)
    {
        type.IsExtern.Should().BeTrue();
        type.DeclType!.ValueString.Should().Be(declKind);
        type.Identifier.Should().Be(identifier);
        type.Extends.Should().BeNull();
        type.Implements.Should().BeNull();
        type.BackingType.Should().BeNull();
        type.Modifiers.Should().BeNull();
        type.Body.Should().NotBeNull();
        type.Body!.GetAllNotNull().Should().BeEmpty();
        type.IsPartial.Should().BeFalse();
        type.IsOmit.Should().BeFalse();
        type.IsDeprecated.Should().BeFalse();
        type.IsObsolete.Should().BeFalse();
        type.ProvidedBy.Should().Be(providedBy);
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
