using System.Text.Json.Nodes;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class ComposerExtraTyhpPackageManifestTests
{
    [Fact]
    public void TryCompose_WritesGeneratedDefaultsWhenFileIsMissing()
    {
        ComposerExtraTyhpPackageManifest.TryCompose(null, isTagless: true, out var json, out var changed, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        changed.Should().BeTrue();

        var package = PackageObject(json);
        StringArray(package, "include").Should().Equal("./package.tyhpdef");
        StringArray(package, "exclude").Should().BeEmpty();
        StringArray(package, "overlay").Should().BeEmpty();
        package["source"]!["tagless"]!.GetValue<bool>().Should().BeTrue();
        JsonNode.Parse(json)!.AsObject().ContainsKey("version").Should().BeFalse();
    }

    [Fact]
    public void TryCompose_AddsMissingKeysAndIncludeItemWithoutAlteringExisting()
    {
        const string existing = """
            {
                "name": "acme/lib",
                "extra": {
                    "class": "Acme\\Plugin",
                    "tyhp": {
                        "interopContractVersion": 1,
                        "require": {
                            "tyhpdef/acme-stubs": "@dev"
                        },
                        "package": {
                            "include": [
                                "./_tyhpdef/*.tyhpdef"
                            ],
                            "overlay": [
                                "./_tyhpdef/overlays/*.tyhpdef"
                            ],
                            "customKey": "keep-me",
                            "source": {
                                "tagless": true
                            }
                        }
                    }
                }
            }
            """;

        ComposerExtraTyhpPackageManifest.TryCompose(existing, isTagless: false, out var json, out var changed, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        changed.Should().BeTrue();

        var root = JsonNode.Parse(json)!.AsObject();
        root["name"]!.GetValue<string>().Should().Be("acme/lib");
        root["extra"]!["class"]!.GetValue<string>().Should().Be("Acme\\Plugin");
        root["extra"]!["tyhp"]!["interopContractVersion"]!.GetValue<int>().Should().Be(1);
        root["extra"]!["tyhp"]!["require"]!["tyhpdef/acme-stubs"]!.GetValue<string>().Should().Be("@dev");

        var package = PackageObject(json);
        StringArray(package, "include").Should().Equal("./_tyhpdef/*.tyhpdef", "./package.tyhpdef");
        StringArray(package, "overlay").Should().Equal("./_tyhpdef/overlays/*.tyhpdef");
        package["customKey"]!.GetValue<string>().Should().Be("keep-me");
        package["source"]!["tagless"]!.GetValue<bool>().Should().BeTrue();
        StringArray(package, "exclude").Should().BeEmpty();
    }

    [Fact]
    public void TryCompose_DoesNotDuplicateNormalizedIncludeGlobs()
    {
        const string existing = """
            {
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["package.tyhpdef"],
                            "exclude": [],
                            "overlay": [],
                            "source": { "tagless": false }
                        }
                    }
                }
            }
            """;

        ComposerExtraTyhpPackageManifest.TryCompose(existing, isTagless: false, out _, out var changed, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        changed.Should().BeFalse();
    }

    [Fact]
    public void TryCompose_RejectsInvalidJsonWithoutProducingOutput()
    {
        ComposerExtraTyhpPackageManifest.TryCompose("{ nope", isTagless: false, out var json, out var changed, out var error)
            .Should().BeFalse();
        json.Should().BeEmpty();
        changed.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Sentinel_RequiresPackageObject()
    {
        var dir = Directory.CreateTempSubdirectory("tyhp-package-sentinel-").FullName;
        try
        {
            var path = Path.Combine(dir, "composer.json");
            File.WriteAllText(path, """{"extra":{"tyhp":{"require":{"tyhpdef/acme-stubs":"@dev"}}}}""");
            ComposerExtraTyhpPackageManifest.HasPackageObject(path).Should().BeFalse();

            File.WriteAllText(path, """{"extra":{"tyhp":{"package":null}}}""");
            ComposerExtraTyhpPackageManifest.HasPackageObject(path).Should().BeFalse();

            File.WriteAllText(path, """{"extra":{"tyhp":{"package":"./package.tyhpdef"}}}""");
            ComposerExtraTyhpPackageManifest.HasPackageObject(path).Should().BeFalse();

            File.WriteAllText(path, """{"extra":{"tyhp":{"package":{}}}}""");
            ComposerExtraTyhpPackageManifest.HasPackageObject(path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("composer.json", true)]
    [InlineData("./pkg/composer.json", true)]
    [InlineData("vendor/*/composer.json", true)]
    [InlineData("vendor\\acme\\lib\\composer.json", true)]
    [InlineData("./pkg/composer.json.dist", false)]
    [InlineData("./pkg/notcomposer.json", false)]
    [InlineData("./pkg/composer.jsonc", false)]
    [InlineData("src/**/*.tyhp", false)]
    public void PatternMayMatchComposerJson_OnlyMatchesPatternsEndingInComposerJson(string pattern, bool expected)
    {
        ComposerExtraTyhpPackageManifest.PatternMayMatchComposerJson(pattern).Should().Be(expected);
    }

    private static JsonObject PackageObject(string composerJson)
    {
        ComposerExtraTyhpPackageManifest.TryGetPackageObject(
                JsonNode.Parse(composerJson)!.AsObject(),
                out var package)
            .Should().BeTrue();
        return package;
    }

    private static List<string> StringArray(JsonObject root, string key)
    {
        return root[key]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
    }
}
