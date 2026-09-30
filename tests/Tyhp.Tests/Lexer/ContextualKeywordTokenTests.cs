using System.Text;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Lexer;

/// <summary>
/// Lexer-only coverage for Tyhp/tyhpdef contextual keywords that must remain valid
/// PHP identifiers outside declaration positions.
/// </summary>
[Trait("Category", "Lexer")]
public class ContextualKeywordTokenTests
{
    [Fact]
    public void Lex_AsyncBeforeFunction_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            async function load(): void {}
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHP_ASYNC);
    }

    [Fact]
    public void Lex_MethodNamedAsync_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            class Worker {
                public function async(): void {}
            }
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHP_ASYNC);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("async", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_MethodNamedOperator_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            class Worker {
                public function operator(): void;
            }
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHP_OPERATOR);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("operator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_OperatorEqualityOverload_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            class Vec {
                operator ==(Vec $left, mixed $right): bool;
            }
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHP_OPERATOR);
    }

    [Fact]
    public void Lex_OperatorOverload_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            class Vec {
                operator +(Vec $left, Vec $right): Vec { return $left; }
            }
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHP_OPERATOR);
    }

    [Fact]
    public void Lex_MethodNamedVoid_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            class Worker {
                public function void(): void;
            }
            """);

        var defaultTokens = tokens.Where(t => t.Type != TyhpLexer.T_TYHP_OPEN_TAG
            && t.Type != TyhpLexer.T_TYHPDEF_OPEN_TAG).ToList();
        defaultTokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("void", StringComparison.OrdinalIgnoreCase));
        defaultTokens.Should().Contain(t => t.Type == TyhpLexer.T_TYHP_VOID);
    }

    [Fact]
    public void Lex_TyhpdefMethodNamedDeprecated_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            class Worker {
                public function deprecated(): void;
            }
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHPDEF_DEPRECATED);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("deprecated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_TyhpdefDeprecatedModifier_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            deprecated function \old_api(): void;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_DEPRECATED);
    }

    [Fact]
    public void Lex_TyhpdefObsoleteModifier_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            obsolete function \gone_api(): void;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_OBSOLETE);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("interface")]
    [InlineData("enum")]
    public void Lex_TyhpdefExternBeforeClassLike_IsKeyword(string kind)
    {
        var tokens = LexDefaultChannel($"<?tyhpdef\nextern {kind} \\Foo\\Bar;\n");

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_EXTERN);
    }

    [Fact]
    public void Lex_TyhpdefExternBeforeFunction_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            extern function \widget_add;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_EXTERN);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_FUNCTION);
    }

    [Fact]
    public void Lex_TyhpdefExternBeforeConst_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            extern const \FOO;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_EXTERN);
        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_CONST);
    }

    [Fact]
    public void Lex_TyhpExternBeforeFunction_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            extern function foo(): void {}
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHPDEF_EXTERN);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("extern", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_TyhpdefBareExtern_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            extern \Foo\Bar;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_EXTERN);
    }

    [Fact]
    public void Lex_TyhpdefMethodNamedExtern_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            class Worker {
                public function extern(): void;
            }
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHPDEF_EXTERN);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("extern", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_TyhpdefExternBeforeTrait_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            extern trait Foo {}
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_EXTERN);
    }

    [Theory]
    [InlineData("class extern extends Base { }")]
    [InlineData("class extern implements Contract { }")]
    public void Lex_TyhpdefTypeNamedExtern_IsTString(string declaration)
    {
        var tokens = LexDefaultChannel($"<?tyhpdef\n{declaration}\n");

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHPDEF_EXTERN);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("extern", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_TyhpExternBeforeClass_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            extern class Foo {}
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHPDEF_EXTERN);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("extern", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Lex_TyhpdefPartialBeforeFunction_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            partial function array_count_values;
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHPDEF_PARTIAL);
    }

    [Fact]
    public void Lex_TyhpdefFunctionNamedPartial_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            function partial(): void;
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHPDEF_PARTIAL);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("partial", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("class struct { }")]
    [InlineData("function struct(): void {}")]
    [InlineData("$x = new struct();")]
    [InlineData("type Point = struct { int $x; };")]
    [InlineData("$x = new struct { int $x; };")]
    public void Lex_StructIsAlwaysTString(string body)
    {
        var tokens = LexDefaultChannel("<?tyhp\n" + body + "\n");

        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("struct", StringComparison.OrdinalIgnoreCase));
        tokens.Where(t => t.Text.Equals("struct", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(t => t.Type == TyhpLexer.T_STRING);
    }

    [Fact]
    public void Lex_InternalBeforeClass_IsKeyword()
    {
        var tokens = LexDefaultChannel("""
            <?tyhp
            internal class Foo {}
            """);

        tokens.Select(t => t.Type).Should().Contain(TyhpLexer.T_TYHP_INTERNAL);
    }

    [Fact]
    public void Lex_TyhpdefInternal_IsTString()
    {
        var tokens = LexDefaultChannel("""
            <?tyhpdef
            class Worker {
                public function internal(): void;
            }
            """);

        tokens.Should().NotContain(t => t.Type == TyhpLexer.T_TYHP_INTERNAL);
        tokens.Should().Contain(t => t.Type == TyhpLexer.T_STRING && t.Text.Equals("internal", StringComparison.OrdinalIgnoreCase));
    }

    private static List<IToken> LexDefaultChannel(string source)
    {
        var contentBytes = Encoding.UTF8.GetBytes(source);
        var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
        var lexer = new TyhpLexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.ConfigureTagless(enabled: false, languageMode: string.Empty, new DiagnosticBag(), fileName: "test.tyhp");

        var stream = new CommonTokenStream(lexer);
        stream.Fill();

        return stream.GetTokens()
            .Where(t => t.Type != TyhpLexer.Eof && t.Channel == Antlr4.Runtime.Lexer.DefaultTokenChannel)
            .ToList();
    }
}
