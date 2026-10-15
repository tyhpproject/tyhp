using System.Text.RegularExpressions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

/// <summary>
/// Found bug #422: the <c>tyhp/compiler</c> version used when the in-tree package cannot be read
/// is the CLI's own release version, which is the GitHub release tag the shim downloads.
/// </summary>
[Trait("Category", "Domain")]
public class CompilerPackageVersionFallbackTests
{
    [Fact]
    public void Fallback_IsTheCliReleaseVersionWithoutBuildMetadata()
    {
        var fallback = ComposerJsonService.CompilerPackageVersionFallback;

        fallback.Should().MatchRegex(new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$").ToString());
        fallback.Should().NotContain("+");
        fallback.Should().Be(new Tyhp.CLI.Message.VersionHelper().GetAssemblyVersion());
    }
}
