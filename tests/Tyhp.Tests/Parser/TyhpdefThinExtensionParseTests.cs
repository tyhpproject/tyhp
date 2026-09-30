using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Story 20.6 Phase 1: class-body tyhpdef <c>extension fn</c> / <c>extension operator</c>
/// are thin <c>=&gt;</c> mappings. Brace bodies are parse errors. Standalone tyhpdef
/// <c>extension Name { }</c> accepts short <c>fn … =&gt;</c> members
/// (optional attributes). <c>function … =&gt;</c> is a parse error on standalone
/// members and on tyhpdef class/enum/trait methods (those stay <c>function … ;</c>
/// signatures or <c>extension fn … =&gt;</c>). Tyhp <c>extension { function { } }</c>
/// is unchanged.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefThinExtensionParseTests
{
    [Fact]
    public void Parse_ClassBodyExtensionFnArrow_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                extension fn toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
        Flatten(result.Ast).OfType<TyhpdefInlineExtensionFunctionAst>().Should().ContainSingle();
    }

    [Fact]
    public void Parse_ClassBodyExtensionOperatorArrow_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Money {
                extension operator +(self $left, self $right): self => $left->plus($right);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
        var op = Flatten(result.Ast).OfType<TyhpOperatorOverloadAst>()
            .Should().ContainSingle(o => o.IsInlineExtension).Subject;
        op.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ClassBodyExtensionFunctionKeywordArrow_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                extension function toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "class-body tyhpdef extension members use extension fn, not extension function");
    }

    [Fact]
    public void Parse_TyhpdefClassFunctionKeywordArrow_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                function toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "tyhpdef class methods are signatures (function … ;) or extension fn … =>, not function … =>");
    }

    [Fact]
    public void Parse_TyhpdefEnumFunctionKeywordArrow_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            enum Holder {
                function toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "tyhpdef enum methods are signatures (function … ;) or extension fn … =>, not function … =>");
    }

    [Fact]
    public void Parse_TyhpdefTraitFunctionKeywordArrow_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            trait Holder {
                function toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "tyhpdef trait methods are signatures (function … ;) or extension fn … =>, not function … =>");
    }

    [Fact]
    public void Parse_ClassBodyExtensionFunctionBrace_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                extension function toUpper(): string { return \strtoupper($this); }
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "class-body tyhpdef extension function { } is not a grammar alternative");
    }

    [Fact]
    public void Parse_ClassBodyExtensionOperatorBrace_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Money {
                extension operator +(self $left, self $right): self { return $left; }
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "class-body tyhpdef extension operator { } is not a grammar alternative");
    }

    [Fact]
    public void Parse_ClassBodyBodylessExtensionOperator_Succeeds()
    {
        // Bodyless still parses; TYHP8013 is a checker diagnostic, not a parse error.
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Money {
                extension operator +(self $left, self $right): self;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
        var op = Flatten(result.Ast).OfType<TyhpOperatorOverloadAst>()
            .Should().ContainSingle(o => o.IsInlineExtension).Subject;
        op.Body.Should().BeNull();
    }

    [Fact]
    public void Parse_StandaloneTyhpdefExtensionFunctionArrow_Unchanged()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extension StringHelpers extends string {
                fn toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
        Flatten(result.Ast).OfType<TyhpdefStandaloneExtensionDeclAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<TyhpdefInlineExtensionFunctionAst>().Should().ContainSingle();
    }

    [Fact]
    public void Parse_StandaloneTyhpdefExtensionFunctionKeywordArrow_Fails()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extension StringHelpers extends string {
                function toUpper(): string => \strtoupper($this);
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "standalone tyhpdef extension short members use fn, not function");
    }

    [Fact]
    public void Parse_StandaloneTyhpdefExtensionMemberAttribute_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extension StringHelpers extends string {
                #[\Tyhp\Optimize\Pure]
                fn length(): int => \strlen($this);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
        var member = Flatten(result.Ast).OfType<TyhpdefInlineExtensionFunctionAst>().Should().ContainSingle().Subject;
        member.AstAttributes.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_TyhpExtensionFunctionBrace_Unchanged()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringHelpers extends string {
                function pad(): string {
                    return $this;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Ast.Should().NotBeNull();
        Flatten(result.Ast).OfType<TyhpExtensionDeclAst>().Should().ContainSingle();
    }

    private static List<IBase2Ast> Flatten(IBase2Ast? root)
    {
        var result = new List<IBase2Ast>();
        if (root is null)
        {
            return result;
        }

        var stack = new Stack<IBase2Ast>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            result.Add(node);
            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }
}
