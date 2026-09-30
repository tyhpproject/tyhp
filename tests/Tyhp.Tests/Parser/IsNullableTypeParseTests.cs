using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Parse coverage for <c>$x is ?T</c> (nullable type spelling on the <c>is</c> RHS).
/// Checker / emit are out of scope here.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class IsNullableTypeParseTests
{
    [Fact]
    public void Parse_IsNonNullableType_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class A {}
            function demo(mixed $x): void {
                $ok = $x is A;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`$x is A` should parse: {Describe(result)}");
        var isOp = FindIs(result.Ast!).Should().ContainSingle().Subject;
        isOp.Right.Should().NotBeAssignableTo<PhpUnaryOpAst>();
    }

    [Theory]
    [InlineData("$x is ?A")]
    [InlineData("$x is ?int")]
    [InlineData("$x is ?Optional<int>")]
    public void Parse_IsNullableType_Succeeds(string expression)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            type Optional<T = mixed> = T|null;
            class A {}
            function demo(mixed $x): void {
                $ok = {{expression}};
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{expression}` should parse: {Describe(result)}");
        var isOp = FindIs(result.Ast!).Should().ContainSingle().Subject;
        var nullable = isOp.Right.Should().BeAssignableTo<PhpUnaryOpAst>().Subject;
        nullable.Operator?.ValueString.Should().Be("?");
        nullable.Operand.Should().NotBeNull();
    }

    [Fact]
    public void Parse_IsThenTernary_DoesNotStealQuestionAsNullable()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class A {}
            function demo(mixed $x): int {
                return $x is A ? 1 : 0;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`$x is A ? 1 : 0` should parse as is-then-ternary: {Describe(result)}");
        var isOp = FindIs(result.Ast!).Should().ContainSingle().Subject;
        isOp.Right.Should().NotBeAssignableTo<PhpUnaryOpAst>(
            "the `?` belongs to the ternary, not a nullable `is` RHS");
    }

    private static IEnumerable<PhpBinaryOpAst> FindIs(IBase2Ast node)
    {
        if (node is PhpBinaryOpAst binary
            && string.Equals(binary.Operator?.ValueString, "is", StringComparison.OrdinalIgnoreCase))
        {
            yield return binary;
        }

        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindIs(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
