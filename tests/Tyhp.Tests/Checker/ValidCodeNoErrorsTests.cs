using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "EndToEnd")]
public class ValidCodeNoErrorsTests
{
    public static IEnumerable<object[]> ValidTyhpFiles()
        => TestFileManager.GetAllTestDataFiles("ValidTyhp/emitter", ".tyhp")
            .Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(ValidTyhpFiles))]
    public void Check_ValidTyhpFixture_ProducesNoCheckerErrors(string filePath)
    {
        using var compilationService = new CompilationService();
        var result = compilationService.ParseFiles(
            [filePath],
            new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

        result.Diagnostics.Errors.Should().BeEmpty($"file should parse/bind/check without errors: {filePath}");
    }
}
