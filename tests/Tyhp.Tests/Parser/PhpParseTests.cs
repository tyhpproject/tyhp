using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class PhpParseTests
{
    public static IEnumerable<object[]> PhpFiles()
        => TestFileManager.GetAllTestDataFiles("ValidPhp/parser", ".php").Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(PhpFiles))]
    public void ParsePhpFile_NoErrors_ForTestDataFile(string filePath)
    {
        var result = ParserTestHelper.ParseFile(filePath);
        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }
}
