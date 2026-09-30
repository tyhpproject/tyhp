using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpRuntimeManagerTests
{
    [Fact]
    public void ResolveRid_IsAKnownRuntimeIdentifier()
    {
        var rid = PhpRuntimeManager.ResolveRid();
        rid.Should().BeOneOf("osx-arm64", "osx-x64", "linux-x64", "linux-arm64", "win-x64");
    }

    [Fact]
    public void ResolveCacheRoot_PrefersExplicitOverrideOverEnv()
    {
        var path = PhpRuntimeManager.ResolveCacheRoot("/tmp/tyhp-php-runtimes");
        path.Should().Be(Path.GetFullPath("/tmp/tyhp-php-runtimes"));
    }

    [Fact]
    public void LoadManifestJson_ContainsProviderIds()
    {
        var json = PhpRuntimeManager.LoadManifestJson();
        json.Should().Contain("staticphp");
        json.Should().Contain("windows-php-net");
        json.Should().Contain("homebrew-bottle");
        json.Should().Contain("dl.static-php.dev");
    }

    [Fact]
    public void WriteTyhpIni_PointsExtensionDirWhenPresent()
    {
        var dir = Directory.CreateTempSubdirectory("tyhp-ini-").FullName;
        try
        {
            var ext = Path.Combine(dir, "ext");
            Directory.CreateDirectory(ext);
            var ini = Path.Combine(dir, "tyhp.ini");
            PhpRuntimeManager.WriteTyhpIni(ini, ext, "json");
            var text = File.ReadAllText(ini);
            text.Should().Contain("extension_dir=");
            text.Should().Contain("Tyhp-managed PHP");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryPickWindowsNtsZip_SelectsLatestPatchOfMinor()
    {
        var json = """
            {
              "8.3.10": {
                "nts": {
                  "vs16": {
                    "x64": {
                      "zip": {
                        "path": "php-8.3.10-nts-Win32-vs16-x64.zip",
                        "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                      }
                    }
                  }
                }
              },
              "8.3.11": {
                "nts": {
                  "vs16": {
                    "x64": {
                      "zip": {
                        "path": "php-8.3.11-nts-Win32-vs16-x64.zip",
                        "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                      }
                    }
                  }
                }
              }
            }
            """;

        PhpRuntimeManager.TryPickWindowsNtsZip(
            json,
            "8.3",
            "https://windows.php.net/downloads/releases/",
            out var url,
            out var version,
            out var sha256).Should().BeTrue();
        version.Should().Be("8.3.11");
        url.Should().Contain("php-8.3.11-nts-Win32-vs16-x64.zip");
        sha256.Should().StartWith("bbbb");
    }
}
