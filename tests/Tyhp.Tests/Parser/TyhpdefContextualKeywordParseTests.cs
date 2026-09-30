using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parser coverage for contextual Tyhp/tyhpdef keywords and PHP reserved words used as
/// member names (e.g. <c>async public function catch&lt;T&gt;(...)</c>).
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefContextualKeywordParseTests
{
    [Fact]
    public void ConstNamedMatch_ParsesAsSemiReservedIdentifier()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class RegexIterator {
                public const int MATCH;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportConstDeclAst>().Should().ContainSingle();
    }

    [Fact]
    public void AsyncMethodNamedCatchWithGenerics_Parses()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Promise {
                async public function catch<TResult>(callable(\Throwable $reason): TResult $onRejected): TReturn|TResult;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var method = Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single();
        method.Identifier.Should().Be("catch");
        method.AstGrammarAddons.Should().ContainKey("isAsync");
        method.AstGrammarAddons.Should().ContainKey("identifier");
    }

    [Fact]
    public void MethodNamedFinally_ParsesAsSemiReservedIdentifier()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Promise {
                async public function finally(callable(): void $onFinally): TReturn;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single().Identifier.Should().Be("finally");
    }

    [Theory]
    [InlineData("async")]
    [InlineData("operator")]
    [InlineData("void")]
    [InlineData("parent")]
    [InlineData("deprecated")]
    [InlineData("obsolete")]
    public void MethodNamedHardKeyword_ParsesAsIdentifier(string name)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(
            "<?tyhpdef\nclass Worker {\n    public function " + name + "(): void;\n}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single().Identifier.Should().Be(name);
    }

    [Theory]
    [InlineData("async")]
    [InlineData("operator")]
    [InlineData("void")]
    [InlineData("deprecated")]
    [InlineData("obsolete")]
    public void TopLevelFunctionNamedHardKeyword_ParsesAsIdentifier(string name)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(
            "<?tyhpdef\nfunction \\" + name + "(): void;\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Should().ContainSingle();
    }

    // A leading `\` (as above) lexes as a single T_NAME_FULLY_QUALIFIED token, bypassing the
    // contextual FOO_AS_T_STRING / T_TYHP_FOO lexer rules entirely. Bare global-function
    // imports exercise the actual contextual lookahead for a global import name.
    [Theory]
    [InlineData("async")]
    [InlineData("operator")]
    [InlineData("void")]
    [InlineData("parent")]
    [InlineData("deprecated")]
    [InlineData("obsolete")]
    public void TopLevelFunctionNamedHardKeyword_Bare_ParsesAsIdentifier(string name)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(
            "<?tyhpdef\nfunction " + name + "(): void;\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single().Identifier.Should().Be(name);
    }

    [Fact]
    public void DeprecatedAndObsoleteModifiers_StillParseAsKeywords()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            deprecated function \old_api(): void;
            obsolete function \gone_api(): void;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var fns = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().ToList();
        fns.Should().HaveCount(2);
        fns[0].IsDeprecated.Should().BeTrue();
        fns[1].IsObsolete.Should().BeTrue();
    }

    [Fact]
    public void EqualityOperatorOverload_StillParsesAsKeyword()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Vec {
                operator ==(Vec $left, mixed $right): bool;
                operator ===(Vec $left, mixed $right): bool;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpOperatorOverloadAst>().Should().HaveCount(2);
    }

    [Fact]
    public void OperatorOverload_StillParsesAsKeyword()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Vec {
                operator +(Vec $left, Vec $right): Vec;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpOperatorOverloadAst>().Should().ContainSingle();
    }

    [Fact]
    public void TyhpMethodNamedOperator_ParsesAsIdentifier()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Worker {
                public function operator(): void {}
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single().Identifier.Should().Be("operator");
    }

    [Fact]
    public void TyhpAsyncFunction_StillParsesAsModifier()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            async function load(): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>().Should().ContainSingle();
    }

    [Fact]
    public void TyhpAsyncBlock_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function go(): void {
                $p = async { return 1; };
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpAsyncBlockAst>().Should().ContainSingle();
    }

    [Fact]
    public void ClassNamedAsync_ParsesAsIdentifier()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class async {
                public function run(): void;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single().Identifier.Should().Be("async");
    }

    // `async` / `operator` (unlike `struct` / `omit` / `partial`) have no explicit
    // `extends\b.` / `implements\b.` exception in their `_AS_T_STRING` predicate: an
    // `extends`/`implements` clause simply fails every "this is being used as a keyword"
    // disjunct, so the identifier alternative wins on its own. Guards that fallback.
    [Theory]
    [InlineData("async")]
    [InlineData("operator")]
    public void ClassNamedHardKeyword_FollowedByExtends_ParsesAsIdentifier(string name)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(
            "<?tyhpdef\nclass Base {\n    public function run(): void;\n}\nclass "
            + name + " extends Base {\n    public function run(): void;\n}\n");

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>()
            .Should().Contain(o => o.Identifier == name);
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
