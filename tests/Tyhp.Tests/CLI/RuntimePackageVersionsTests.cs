using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Build")]
public class RuntimePackageVersionsTests
{
    [Fact]
    public void BundledVersions_MatchEachPackageComposerJson()
    {
        var packagesDir = TestFileManager.GetRuntimePackagesDirectory();
        foreach (var (packageName, expected) in RuntimePackageVersions.Bundled)
        {
            var folder = packageName["tyhp/".Length..];
            var composerPath = Path.Combine(packagesDir, folder, "composer.json");
            File.Exists(composerPath).Should().BeTrue($"missing {composerPath}");
            RuntimePackageVersions.TryReadComposerVersion(Path.Combine(packagesDir, folder))
                .Should().Be(expected, because: $"{packageName} must version independently of the compiler");
        }
    }
}
