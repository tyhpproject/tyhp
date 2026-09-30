using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Story 20.6 Phase 2: <c>IsShortSyntax</c> distinguishes visit-time <c>=&gt;</c>
/// desugaring from an authored single-return brace body, and survives AST cache
/// serialize/deserialize.
/// </summary>
[Trait("Category", "Parser")]
public class IsShortSyntaxFlagTests
{
    [Fact]
    public void Parse_ShortFunction_SetsIsShortSyntax()
    {
        var fn = ParseTyhpSingle<PhpFunctionDeclAst>("""
            <?tyhp
            fn f(): int => 1;
            """);
        fn.Identifier.Should().Be("f");
        fn.IsShortSyntax.Should().BeTrue();
        fn.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_BraceFunction_DoesNotSetIsShortSyntax()
    {
        var fn = ParseTyhpSingle<PhpFunctionDeclAst>("""
            <?tyhp
            function f(): int { return 1; }
            """);
        fn.Identifier.Should().Be("f");
        fn.IsShortSyntax.Should().BeFalse();
    }

    [Fact]
    public void Parse_ShortMethod_SetsIsShortSyntax()
    {
        var method = ParseTyhpSingle<PhpMethodDeclAst>("""
            <?tyhp
            class C {
                public fn m(): int => 1;
            }
            """);
        method.Identifier.Should().Be("m");
        method.IsShortSyntax.Should().BeTrue();
    }

    [Fact]
    public void Parse_BraceMethod_DoesNotSetIsShortSyntax()
    {
        var method = ParseTyhpSingle<PhpMethodDeclAst>("""
            <?tyhp
            class C {
                public function m(): int { return 1; }
            }
            """);
        method.Identifier.Should().Be("m");
        method.IsShortSyntax.Should().BeFalse();
    }

    [Fact]
    public void Parse_ShortOperator_SetsIsShortSyntax()
    {
        var op = ParseTyhpSingle<TyhpOperatorOverloadAst>("""
            <?tyhp
            class Money {
                operator +(self $left, self $right): self => $left;
            }
            """);
        op.IsShortSyntax.Should().BeTrue();
        op.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_BraceOperator_DoesNotSetIsShortSyntax()
    {
        var op = ParseTyhpSingle<TyhpOperatorOverloadAst>("""
            <?tyhp
            class Money {
                operator +(self $left, self $right): self {
                    return $left;
                }
            }
            """);
        op.IsShortSyntax.Should().BeFalse();
        op.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ShortExtensionOperator_SetsIsShortSyntax()
    {
        var op = ParseTyhpSingle<TyhpOperatorOverloadAst>("""
            <?tyhp
            extension MoneyOperators extends Money {
                operator + (self $left, self $right): self => $left;
            }
            """);
        op.IsShortSyntax.Should().BeTrue();
        op.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_BraceExtensionOperator_DoesNotSetIsShortSyntax()
    {
        var op = ParseTyhpSingle<TyhpOperatorOverloadAst>("""
            <?tyhp
            extension MoneyOperators extends Money {
                operator + (self $left, self $right): self {
                    return $left;
                }
            }
            """);
        op.IsShortSyntax.Should().BeFalse();
        op.Body.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ShortExtensionFunction_SetsIsShortSyntax()
    {
        var fn = ParseTyhpSingle<PhpFunctionDeclAst>("""
            <?tyhp
            extension StringHelpers extends string {
                fn pad(): string => $this;
            }
            """);
        fn.Identifier.Should().Be("pad");
        fn.IsShortSyntax.Should().BeTrue();
    }

    [Fact]
    public void Parse_BraceExtensionFunction_DoesNotSetIsShortSyntax()
    {
        var fn = ParseTyhpSingle<PhpFunctionDeclAst>("""
            <?tyhp
            extension StringHelpers extends string {
                function pad(): string {
                    return $this;
                }
            }
            """);
        fn.Identifier.Should().Be("pad");
        fn.IsShortSyntax.Should().BeFalse();
    }

    [Fact]
    public void Parse_TyhpdefClassBodyExtensionFn_SetsIsShortSyntaxOnMethod()
    {
        var wrapper = ParseTyhpdefSingle<TyhpdefInlineExtensionFunctionAst>("""
            <?tyhpdef
            class Holder {
                extension fn toUpper(): string => \strtoupper($this);
            }
            """);
        wrapper.Method.Should().NotBeNull();
        wrapper.Method!.IsShortSyntax.Should().BeTrue();
    }

    [Fact]
    public void Parse_TyhpdefClassBodyExtensionOperatorArrow_SetsIsShortSyntax()
    {
        var op = ParseTyhpdefSingle<TyhpOperatorOverloadAst>("""
            <?tyhpdef
            class Money {
                extension operator +(self $left, self $right): self => $left->plus($right);
            }
            """);
        op.IsShortSyntax.Should().BeTrue();
        op.IsInlineExtension.Should().BeTrue();
    }

    [Fact]
    public void Parse_TyhpdefBodylessExtensionOperator_DoesNotSetIsShortSyntax()
    {
        var op = ParseTyhpdefSingle<TyhpOperatorOverloadAst>("""
            <?tyhpdef
            class Money {
                extension operator +(self $left, self $right): self;
            }
            """);
        op.IsShortSyntax.Should().BeFalse();
        op.Body.Should().BeNull();
    }

    [Fact]
    public void SerializeDeserialize_PreservesIsShortSyntaxOnFunctionMethodAndOperator()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            fn shortFn(): int => 1;
            function braceFn(): int { return 1; }

            class C {
                public fn shortM(): int => 1;
                public function braceM(): int { return 1; }
                operator +(self $left, self $right): self => $left;
                operator -(self $left, self $right): self {
                    return $left;
                }
            }
            """);
        AssertNoErrors(result);
        result.Ast.Should().NotBeNull();

        var bytes = result.Ast!.Serialize();
        var roundTripped = Base2Ast.Deserialize<SrcFileAst>(bytes, skipChildrenFlagsAndAttributes: false);

        var functions = Flatten(roundTripped).OfType<PhpFunctionDeclAst>().ToDictionary(f => f.Identifier);
        functions["shortFn"].IsShortSyntax.Should().BeTrue();
        functions["braceFn"].IsShortSyntax.Should().BeFalse();

        var methods = Flatten(roundTripped).OfType<PhpMethodDeclAst>().ToDictionary(m => m.Identifier);
        methods["shortM"].IsShortSyntax.Should().BeTrue();
        methods["braceM"].IsShortSyntax.Should().BeFalse();

        var operators = Flatten(roundTripped).OfType<TyhpOperatorOverloadAst>().ToList();
        operators.Should().HaveCount(2);
        operators.Single(o => o.Identifier == "+").IsShortSyntax.Should().BeTrue();
        operators.Single(o => o.Identifier == "-").IsShortSyntax.Should().BeFalse();
    }

    [Fact]
    public void SerializeDeserialize_PreservesIsShortSyntaxOnTyhpdefExtensionMembers()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Money {
                extension fn toUpper(): string => \strtoupper($this);
                extension operator +(self $left, self $right): self => $left->plus($right);
                extension operator -(self $left, self $right): self;
            }
            """);
        AssertNoErrors(result);
        result.Ast.Should().NotBeNull();

        var bytes = result.Ast!.Serialize();
        var roundTripped = Base2Ast.Deserialize<SrcFileAst>(bytes, skipChildrenFlagsAndAttributes: false);

        Flatten(roundTripped).OfType<TyhpdefInlineExtensionFunctionAst>().Should().ContainSingle()
            .Which.Method!.IsShortSyntax.Should().BeTrue();

        var operators = Flatten(roundTripped).OfType<TyhpOperatorOverloadAst>().ToList();
        operators.Single(o => o.Identifier == "+").IsShortSyntax.Should().BeTrue();
        operators.Single(o => o.Identifier == "+").IsInlineExtension.Should().BeTrue();
        operators.Single(o => o.Identifier == "-").IsShortSyntax.Should().BeFalse();
        operators.Single(o => o.Identifier == "-").IsInlineExtension.Should().BeTrue();
    }

    [Fact]
    public void SerializeDeserialize_PreservesBlockTargetOnExtensionDeclaration()
    {
        // Story 27.2: the operator target moved off the individual `operator` node
        // (retired `ExtensionTargetType` on TyhpOperatorOverloadAst for block members)
        // onto the extension header / nested `extends` group instead. Those are
        // grammar addons (TargetType, GenericParameters) plus a real flag bit
        // (IsTargetGroup) specifically so they survive AstCacheService
        // serialize/deserialize — see Ast/technical-guide.md "Pitfalls".
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Money {}
            extension MoneyOperators<T> extends Money {
                operator + (self $left, self $right): self => $left;

                extends<TRight> \App\Pair<string, TRight> {
                    function right(): TRight { return $this->right; }
                }
            }
            """);
        AssertNoErrors(result);
        result.Ast.Should().NotBeNull();

        var live = Flatten(result.Ast).OfType<TyhpExtensionDeclAst>()
            .Should().ContainSingle(decl => !decl.IsTargetGroup).Subject;
        live.TargetType.Should().NotBeNull();
        live.GenericParameters.Should().NotBeNull();
        var liveGroup = live.FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>()
            .Should().ContainSingle().Subject;
        liveGroup.IsTargetGroup.Should().BeTrue();
        liveGroup.TargetType.Should().NotBeNull();
        liveGroup.GenericParameters.Should().NotBeNull();

        var bytes = result.Ast!.Serialize();
        var roundTripped = Base2Ast.Deserialize<SrcFileAst>(bytes, skipChildrenFlagsAndAttributes: false);

        var decl = Flatten(roundTripped).OfType<TyhpExtensionDeclAst>()
            .Should().ContainSingle(d => !d.IsTargetGroup).Subject;
        decl.TargetType.Should().NotBeNull();
        decl.GenericParameters.Should().NotBeNull();
        decl.GenericParameters!.GetAllNotNull().Select(p => p.Identifier).Should().Equal("T");

        var group = decl.FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>()
            .Should().ContainSingle().Subject;
        group.IsTargetGroup.Should().BeTrue();
        group.TargetType.Should().NotBeNull();
        group.GenericParameters!.GetAllNotNull().Select(p => p.Identifier).Should().Equal("TRight");

        var op = Flatten(roundTripped).OfType<TyhpOperatorOverloadAst>().Should().ContainSingle().Subject;
        op.IsShortSyntax.Should().BeTrue();
        op.IsInlineExtension.Should().BeFalse();
        op.ExtensionTargetType.Should().BeNull();
    }

    private static T ParseTyhpSingle<T>(string source) where T : IBase2Ast
    {
        var result = ParserTestHelper.ParseTyhpContent(source);
        AssertNoErrors(result);
        result.Ast.Should().NotBeNull();
        return Flatten(result.Ast).OfType<T>().Should().ContainSingle().Subject;
    }

    private static T ParseTyhpdefSingle<T>(string source) where T : IBase2Ast
    {
        var result = ParserTestHelper.ParseTyhpdefContent(source);
        AssertNoErrors(result);
        result.Ast.Should().NotBeNull();
        return Flatten(result.Ast).OfType<T>().Should().ContainSingle().Subject;
    }

    private static void AssertNoErrors(ParseResult result)
        => result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

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
