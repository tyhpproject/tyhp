using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class RuntimePackageTyhpdefParseTests
{
    public static IEnumerable<object[]> RuntimePackageTyhpdefFiles()
        => TestFileManager.GetAllRuntimePackageTyhpdefFiles().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(RuntimePackageTyhpdefFiles))]
    public void ParseRuntimePackageTyhpdefFile_NoErrors(string filePath)
    {
        var result = ParserTestHelper.ParseFile(filePath, tagless: false);
        result.Diagnostics.Errors.Should().BeEmpty($"failed parsing {filePath}");
        result.Ast.Should().NotBeNull();
    }
}
