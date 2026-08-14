using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class ExtensionTyhpdefParseTests
{
    public static IEnumerable<object[]> ExtensionTyhpdefFiles()
        => TestFileManager.GetAllExtensionTyhpdefFiles().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(ExtensionTyhpdefFiles))]
    public void ParseExtensionTyhpdefFile_NoErrors(string filePath)
    {
        var result = ParserTestHelper.ParseFile(filePath, tagless: false);
        result.Diagnostics.Errors.Should().BeEmpty($"failed parsing {filePath}");
        result.Ast.Should().NotBeNull();
    }
}
