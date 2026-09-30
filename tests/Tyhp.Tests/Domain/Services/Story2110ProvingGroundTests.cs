using System.Text.Json;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

/// <summary>
/// Story 21.10 Phase 7 proving ground: consumer composer fixtures after sync
/// keep compiler + php stubs in require-dev, not extension wrappers.
/// </summary>
[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story21.10")]
public class Story2110ProvingGroundTests
{
    [Fact]
    public void AppPostSyncComposerFixture_HasCompilerPhp_NotExtensionWrappers()
    {
        var path = Path.Combine(
            TestFileManager.GetTestProjectDirectory(),
            "Fixtures",
            "Story2110",
            "app-post-sync.composer.json");
        File.Exists(path).Should().BeTrue(path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var require = root.GetProperty("require");
        require.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["php", "tyhp/core", "tyhp/decimal"]);
        require.GetProperty("tyhp/core").GetString().Should().NotBeNullOrWhiteSpace();
        require.GetProperty("tyhp/decimal").GetString().Should().NotBeNullOrWhiteSpace();

        var requireDev = root.GetProperty("require-dev");
        requireDev.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["tyhp/compiler", "tyhpdef/php"]);
        requireDev.GetProperty("tyhp/compiler").GetString().Should().NotBeNullOrWhiteSpace();
        requireDev.GetProperty("tyhpdef/php").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
