using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class TyhpParseTests
{
    public static IEnumerable<object[]> TyhpFiles()
        => TestFileManager.GetAllTestDataFiles("ValidTyhp/parser", ".tyhp").Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(TyhpFiles))]
    public void ParseTyhpFile_NoErrors_ForTestDataFile(string filePath)
    {
        var result = ParserTestHelper.ParseFile(filePath, tagless: false);
        result.Diagnostics.Errors.Should().BeEmpty($"parse errors in {filePath}: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Code))}");
        result.Ast.Should().NotBeNull();
        result.Ast!.AstChildren.Count.Should().BeGreaterThan(0);
    }
}
