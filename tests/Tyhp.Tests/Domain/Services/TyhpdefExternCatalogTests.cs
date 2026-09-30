using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class TyhpdefExternCatalogTests
{
    [Fact]
    public void Index_DocblockBracesAfterUrl_DoNotPopNamespace()
    {
        var root = Directory.CreateTempSubdirectory("tyhpdef-catalog-doc-").FullName;
        try
        {
            WritePackage(
                root,
                "acme-catalog",
                "tyhpdef/acme-catalog",
                """{ "acme/catalog": "0.1" }""",
                """
                <?tyhpdef
                namespace Acme\Catalog {
                    /**
                     * REST: https://example.com/locations/{pool_id}/group/{group_id
                     * }
                     */
                    class Environment {}
                    class Source {}
                    class Node {}
                }
                """);

            var catalog = TyhpdefExternCatalog.Build([root]);
            catalog.ByFqcn.Keys.Should().Contain("\\Acme\\Catalog\\Environment");
            catalog.ByFqcn.Keys.Should().Contain("\\Acme\\Catalog\\Source");
            catalog.ByFqcn.Keys.Should().Contain("\\Acme\\Catalog\\Node");
            catalog.ByFqcn.Keys.Should().NotContain("\\Environment");
            catalog.ByFqcn.Keys.Should().NotContain("\\Source");
            catalog.ByFqcn.Keys.Should().NotContain("\\Node");

            var shorts = catalog.UniqueGlobalAndPsrShortNames();
            shorts.Should().NotContainKey("Environment");
            shorts.Should().NotContainKey("Source");
            shorts.Should().NotContainKey("Node");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Index_StringBraces_DoNotPopNamespace()
    {
        var root = Directory.CreateTempSubdirectory("tyhpdef-catalog-str-").FullName;
        try
        {
            WritePackage(
                root,
                "acme-markup",
                "tyhpdef/acme-markup",
                """{ "acme/markup": "3.0" }""",
                """
                <?tyhpdef
                namespace Acme\Markup {
                    const string Path ?? "/groups/{group_id}";
                    class Environment {}
                }
                namespace Acme\Markup\Node {
                    class Node {}
                }
                """);

            var catalog = TyhpdefExternCatalog.Build([root]);
            catalog.ByFqcn.Keys.Should().Contain("\\Acme\\Markup\\Environment");
            catalog.ByFqcn.Keys.Should().Contain("\\Acme\\Markup\\Node\\Node");
            catalog.ByFqcn.Keys.Should().NotContain("\\Environment");
            catalog.ByFqcn.Keys.Should().NotContain("\\Node");
            catalog.UniqueGlobalAndPsrShortNames().Should().NotContainKey("Environment");
            catalog.UniqueGlobalAndPsrShortNames().Should().NotContainKey("Node");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UniqueGlobalAndPsrShortNames_LibraryGlobal_IsNotPromoted()
    {
        var root = Directory.CreateTempSubdirectory("tyhpdef-catalog-lib-").FullName;
        try
        {
            WritePackage(
                root,
                "acme-lib",
                "tyhpdef/acme-lib",
                """{ "acme/lib": "1.0" }""",
                """
                <?tyhpdef
                class Environment {}
                class Enum {}
                class Connection {}
                """);

            var shorts = TyhpdefExternCatalog.Build([root]).UniqueGlobalAndPsrShortNames();
            shorts.Should().BeEmpty();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UniqueGlobalAndPsrShortNames_PhpAndPsr_StayUnique()
    {
        var root = Directory.CreateTempSubdirectory("tyhpdef-catalog-php-").FullName;
        try
        {
            WritePackage(
                root,
                "acme-stubs",
                "tyhpdef/acme-stubs",
                """{ "php": ">=8.2" }""",
                """
                <?tyhpdef
                class DateTime {}
                """);
            WritePackage(
                root,
                "acme-cache",
                "tyhpdef/acme-cache",
                """{ "acme/cache": "^3.0" }""",
                """
                <?tyhpdef
                namespace Psr\Acme {
                    interface WidgetPool {}
                }
                """);
            WritePackage(
                root,
                "acme-catalog",
                "tyhpdef/acme-catalog",
                """{ "acme/catalog": "0.1" }""",
                """
                <?tyhpdef
                namespace Acme\Catalog {
                    class Environment {}
                }
                """);

            var shorts = TyhpdefExternCatalog.Build([root]).UniqueGlobalAndPsrShortNames();
            shorts.Should().ContainKey("DateTime").WhoseValue.Should().Be("\\DateTime");
            shorts.Should().ContainKey("WidgetPool")
                .WhoseValue.Should().Be("\\Psr\\Acme\\WidgetPool");
            shorts.Should().NotContainKey("Environment");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PhpBuiltins_MapAlwaysPresentExtensions()
    {
        var root = Directory.CreateTempSubdirectory("tyhpdef-catalog-ext-").FullName;
        try
        {
            var dir = Path.Combine(root, "acme-stubs");
            Directory.CreateDirectory(Path.Combine(dir, "_tyhpdef"));
            File.WriteAllText(Path.Combine(dir, "composer.json"), """
                {
                    "name": "tyhpdef/acme-stubs",
                    "version": "0.0.1",
                    "require": { "php": ">=8.2" },
                    "extra": {
                        "tyhp": {
                            "extensions": ["Core", "json", "hash", "widgetext"],
                            "package": { "include": ["./_tyhpdef/*.tyhpdef"] }
                        }
                    }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "_tyhpdef", "php.tyhpdef"), """
                <?tyhpdef
                class DateTime {}
                """);

            var catalog = TyhpdefExternCatalog.Build([root]);
            catalog.TryGetWrapperForPhpPackage("php", out var php).Should().BeTrue();
            php.Should().Be("tyhpdef/acme-stubs");
            catalog.TryGetWrapperForPhpPackage("ext-json", out var json).Should().BeTrue();
            json.Should().Be("tyhpdef/acme-stubs");
            catalog.TryGetWrapperForPhpPackage("ext-hash", out var hash).Should().BeTrue();
            hash.Should().Be("tyhpdef/acme-stubs");
            catalog.TryGetWrapperForPhpPackage("ext-Core", out var core).Should().BeTrue();
            core.Should().Be("tyhpdef/acme-stubs");
            catalog.TryGetWrapperForPhpPackage("ext-widgetext", out _).Should().BeFalse();
            catalog.ByFqcn["\\DateTime"].WrappedPhpPackages.Should().Equal("php");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void WritePackage(
        string root,
        string folder,
        string tyhpName,
        string requireJson,
        string tyhpdefBody)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(Path.Combine(dir, "_tyhpdef"));
        File.WriteAllText(Path.Combine(dir, "composer.json"), $$"""
            {
                "name": "{{tyhpName}}",
                "version": "1.0.0",
                "require": {{requireJson}},
                "extra": { "tyhp": { "package": { "include": ["./_tyhpdef/*.tyhpdef"] } } }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "_tyhpdef", folder + ".tyhpdef"), tyhpdefBody);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
