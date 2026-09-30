using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Emitter-fixture checker smoke. Language-rule fixtures compile against
/// <see cref="IsolatedCompilation"/> stubs.
/// </summary>
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
        var result = IsolatedCompilation.ParseExistingFiles([filePath], phpVersion: "8.4");
        result.Diagnostics.Errors.Should().BeEmpty($"file should parse/bind/check without errors: {filePath}");
    }
}
