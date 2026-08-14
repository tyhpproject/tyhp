using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefParseTests
{
    public static IEnumerable<object[]> TyhpdefFiles()
        => TestFileManager.GetAllTestDataFiles("ValidTyhpdef/parser", ".tyhpdef").Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(TyhpdefFiles))]
    public void ParseTyhpdefFile_NoErrors_ForTestDataFile(string filePath)
    {
        var result = ParserTestHelper.ParseFile(filePath, tagless: false);
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }
}
