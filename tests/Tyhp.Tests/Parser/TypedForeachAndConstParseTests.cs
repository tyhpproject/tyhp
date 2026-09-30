using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// FOUND_BUGS #51 / #52 parse coverage: typed foreach bindings are legal Tyhp,
/// and file-level <c>const int X</c> parses (checker rejects it).
/// </summary>
[Trait("Category", "Parser")]
public class TypedForeachAndConstParseTests
{
    [Theory]
    [InlineData("foreach ($xs as string $v) {}", true, false)]
    [InlineData("foreach ($xs as int $k => string $v) {}", true, true)]
    [InlineData("foreach ($xs as int $k => $v) {}", false, true)]
    [InlineData("foreach ($xs as $k => string $v) {}", true, false)]
    [InlineData("foreach ($xs as $v) {}", false, false)]
    [InlineData("foreach ($xs as array<int> $v) {}", true, false)]
    public void Parse_TypedForeach_Succeeds(string statement, bool valueTyped, bool keyTyped)
    {
        var result = ParserTestHelper.ParseTyhpContent($$"""
            <?tyhp
            function demo(array<int, string> $xs): void {
                {{statement}}
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"`{statement}` should parse: {Describe(result)}");
        var loop = FindForeach(result.Ast!);
        loop.Should().NotBeNull();
        HasType(loop!.ValueVariable).Should().Be(valueTyped, "value binding type");
        HasType(loop.KeyVariable).Should().Be(keyTyped, "key binding type");
    }

    [Fact]
    public void Parse_FileLevelUntypedConst_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            const LIMIT = 10;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void Parse_FileLevelTypedConst_SucceedsSoCheckerCanReject()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            const int LIMIT = 10;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            "typed file-level const must parse (not TYHP1002) so the checker can report TYHP4193: "
            + Describe(result));
        FindFileLevelConsts(result.Ast!)
            .Should().Contain(c => c.Type != null && c.Identifier == "LIMIT");
    }

    [Fact]
    public void Parse_InternalFileLevelTypedConst_SucceedsSoCheckerCanReject()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            internal const int MAX_RETRIES = 3;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            "internal typed file-level const must parse: " + Describe(result));
        FindFileLevelConsts(result.Ast!)
            .Should().Contain(c => c.Type != null && c.Identifier == "MAX_RETRIES");
    }

    [Fact]
    public void Parse_TyhpdefFileLevelTypedConst_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            const int LIMIT;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            "tyhpdef file-level typed const is legal: " + Describe(result));
    }

    private static PhpLoopAst? FindForeach(IBase2Ast node)
    {
        if (node is PhpLoopAst loop
            && loop.LoopType == Tyhp.TyhpLang.Enum.PhpLoopType.Foreach)
        {
            return loop;
        }

        foreach (var child in node.AstChildren)
        {
            if (child is not null && FindForeach(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static bool HasType(IExpression? variable) =>
        variable is PhpVariableAst { Type: not null };

    private static List<PhpConstDeclAst> FindFileLevelConsts(IBase2Ast node)
    {
        var found = new List<PhpConstDeclAst>();
        Collect(node, found);
        return found;
    }

    private static void Collect(IBase2Ast node, List<PhpConstDeclAst> found)
    {
        if (node is PhpConstDeclAst constant)
        {
            found.Add(constant);
        }

        foreach (var child in node.AstChildren)
        {
            if (child is not null)
            {
                Collect(child, found);
            }
        }
    }

    private static string Describe(ParseResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
