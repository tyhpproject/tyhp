using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Config;

[Trait("Category", "Config")]
public class RequireDevSourceExclusionTests
{
    [Fact]
    public void GetProjectSourceFiles_ExcludesRequireDevOnlyTyhpSources()
    {
        using var builder = new TestProjectBuilder()
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/", "phpVersion": "8.2" }
                }
                """)
            .WithTyhpFile("composer.json", """
                {
                    "require": {
                        "acme/stubs": "@dev",
                        "acme/widget": "@dev"
                    },
                    "require-dev": {
                        "tyhpdef/acme-extra": "@dev"
                    }
                }
                """)
            .WithTyhpFile("src/app.tyhp", """
                <?tyhp
                function app_entry(): void {}
                """)
            .WithTyhpFile("vendor/acme/widget/tyhp_src/from_require.tyhp", """
                <?tyhp
                function from_require(): void {}
                """)
            .WithTyhpFile("vendor/tyhpdef/acme-extra/tyhp_src/from_require_dev.tyhp", """
                <?tyhp
                function from_require_dev(): void {}
                """);

        var files = builder.BuildProject().GetProjectSourceFiles()
            .Select(path => path.Replace('\\', '/'))
            .ToList();

        files.Should().Contain(path => path.EndsWith("/src/app.tyhp", StringComparison.OrdinalIgnoreCase));
        files.Should().Contain(path => path.EndsWith("/from_require.tyhp", StringComparison.OrdinalIgnoreCase));
        files.Should().NotContain(path => path.EndsWith("/from_require_dev.tyhp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetProjectSourceFiles_KeepsTyhpWhenPackageIsAlsoInRequire()
    {
        using var builder = new TestProjectBuilder()
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("composer.json", """
                {
                    "require": {
                        "acme/widget": "@dev"
                    },
                    "require-dev": {
                        "acme/widget": "@dev"
                    }
                }
                """)
            .WithTyhpFile("src/app.tyhp", """
                <?tyhp
                function app_entry(): void {}
                """)
            .WithTyhpFile("vendor/acme/widget/tyhp_src/also_required.tyhp", """
                <?tyhp
                function also_required(): void {}
                """);

        var files = builder.BuildProject().GetProjectSourceFiles()
            .Select(path => path.Replace('\\', '/'))
            .ToList();

        files.Should().Contain(path => path.EndsWith("/also_required.tyhp", StringComparison.OrdinalIgnoreCase));
    }
}
