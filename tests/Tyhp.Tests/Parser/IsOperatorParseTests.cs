using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// <c>is</c> and PHP <c>instanceof</c> parse as the type-check operator.
/// Removed spellings do not. <c>\is_a(...)</c> is a function call.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class IsOperatorParseTests
{
    [Fact]
    public void Parse_Is_IsInstanceofLikeBinary()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Foo {}
            function demo(mixed $x): void {
                $ok = $x is Foo;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`$x is Foo` should parse: {Describe(result)}");
        var op = FindInstanceofLike(result.Ast!).Should().ContainSingle().Subject;
        op.Operator?.ValueString.Should().Be("is");
    }

    [Fact]
    public void Parse_Instanceof_IsInstanceofLikeBinary()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Foo {}
            function demo(mixed $x): void {
                $ok = $x instanceof Foo;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`$x instanceof Foo` should parse: {Describe(result)}");
        var op = FindInstanceofLike(result.Ast!).Should().ContainSingle().Subject;
        op.Operator?.ValueString.Should().Be("instanceof");
    }

    [Theory]
    [InlineData("isa")]
    [InlineData("isan")]
    [InlineData("is_a")]
    [InlineData("is_an")]
    public void Parse_RemovedSpellings_AreNotInstanceof(string spelling)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            class Foo {}
            function demo(mixed $x): void {
                $ok = $x {{spelling}} Foo;
            }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            $"`$x {spelling} Foo` must not parse as instanceof");

        if (result.Ast is null)
        {
            return;
        }

        FindInstanceofLike(result.Ast).Should().BeEmpty(
            $"`$x {spelling} Foo` must not produce an instanceof-like binary op");
    }

    [Fact]
    public void Parse_IsAFunctionCall_IsCallNotOperator()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class User {}
            function demo(object $obj): void {
                $ok = \is_a($obj, User::class);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`\\is_a($obj, User::class)` should parse as a call: {Describe(result)}");
        result.Ast.Should().NotBeNull();

        FindInstanceofLike(result.Ast!).Should().BeEmpty(
            "`\\is_a(...)` must not parse as the instanceof operator");

        var calls = FindCallsNamed(result.Ast!, "is_a").ToList();
        calls.Should().ContainSingle("`\\is_a(...)` should be a function call");
    }

    private static IEnumerable<PhpBinaryOpAst> FindBinaryOps(IBase2Ast node)
    {
        if (node is PhpBinaryOpAst binary)
        {
            yield return binary;
        }

        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindBinaryOps(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<PhpBinaryOpAst> FindInstanceofLike(IBase2Ast node) =>
        FindBinaryOps(node).Where(binary =>
        {
            var opText = binary.Operator?.ValueString;
            return string.Equals(opText, "instanceof", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_an", StringComparison.OrdinalIgnoreCase);
        });

    private static IEnumerable<PhpDereferenceableAst> FindCallsNamed(IBase2Ast node, string name)
    {
        if (node is PhpDereferenceableAst deref
            && deref.Suffix is PhpCallAst
            && NameContains(deref.Base, name))
        {
            yield return deref;
        }

        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindCallsNamed(child, name))
            {
                yield return nested;
            }
        }
    }

    private static bool NameContains(IBase2Ast? node, string name)
    {
        if (node is null)
        {
            return false;
        }

        if (node.ValueString is string text
            && text.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node.AstChildren.Any(child => NameContains(child, name));
    }

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
