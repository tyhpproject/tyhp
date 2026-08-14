using Microsoft.Extensions.Configuration;
using Tyhp.Config;

namespace Tyhp.Tests.Config;

[Trait("Category", "Config")]
public class ProjectConfigTests
{
    [Fact]
    public void Project_Defaults_WhenConfigurationEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();
        var project = new Project(configuration);

        project.Type.Should().Be(ProjectType.Application);
        project.Output.Path.Should().Be("build/");
        project.Output.PhpVersion.Should().Be("8.4");
        project.Output.StrictTypes.Should().BeTrue();
        project.Output.IncludeComments.Should().BeTrue();
        project.Build.GenerateTyhpdef.Should().BeFalse();
        project.Build.CleanBeforeBuild.Should().BeFalse();
        project.Build.StrictMode.Should().BeFalse();
        project.Checker.MaxFixIterations.Should().Be(10);
        project.Checker.TemplateStringMaxStates.Should().Be(256);
        project.Tagless.Should().BeFalse();
        project.PhpVersion.Should().Be("8.4");
    }

    [Fact]
    public void Project_LibraryType_DefaultsGenerateTyhpdefToTrue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = "library",
            })
            .Build();

        var project = new Project(configuration);

        project.Type.Should().Be(ProjectType.Library);
        project.Build.GenerateTyhpdef.Should().BeTrue();
    }

    [Fact]
    public void Project_ApplicationType_WithExplicitGenerateTyhpdefFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = "application",
                ["build:generateTyhpdef"] = "true",
            })
            .Build();

        var project = new Project(configuration);

        project.Type.Should().Be(ProjectType.Application);
        project.Build.GenerateTyhpdef.Should().BeTrue();
    }

    [Fact]
    public void Project_ParsesOutputAndBuildSections()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:path"] = "dist/",
                ["output:namespacePrefix"] = "Vendor\\Pkg",
                ["output:comments"] = "false",
                ["output:phpVersion"] = "8.2",
                ["output:strictTypes"] = "false",
                ["build:generateSourcemap"] = "true",
                ["build:sourcemapIncludeContent"] = "true",
                ["build:updateComposer"] = "true",
                ["build:structBacking"] = "array",
                ["build:decimalBacking"] = "gmp",
                ["build:decimalScale"] = "18",
                ["build:decimalRounding"] = "halfEven",
                ["build:allowEval"] = "true",
                ["build:profile"] = "release",
                ["build:optimize"] = "aggressive",
                ["build:runtimeGenericChecks"] = "true",
                ["psr4:App\\"] = "src/",
                ["psr4Includes:0"] = "extra/",
            })
            .Build();

        var project = new Project(configuration);

        project.Output.Path.Should().Be("dist/");
        project.Output.NamespacePrefix.Should().Be("Vendor\\Pkg");
        project.Output.IncludeComments.Should().BeFalse();
        project.Output.PhpVersion.Should().Be("8.2");
        project.Output.StrictTypes.Should().BeFalse();
        project.Build.GenerateSourcemap.Should().BeTrue();
        project.Build.SourceMapIncludeContent.Should().BeTrue();
        project.Build.UpdateComposer.Should().BeTrue();
        project.Build.DecimalBacking.Should().Be("gmp");
        project.Build.DecimalScale.Should().Be(18);
        project.Build.DecimalRounding.Should().Be("halfEven");
        project.Build.AllowEval.Should().BeTrue();
        project.Build.Profile.Should().Be("release");
        project.Build.Optimize.Should().Be("aggressive");
        project.Build.RuntimeGenericChecks.Should().BeTrue();
        project.Build.Psr4.Should().ContainKey("App\\").WhoseValue.Should().Be("src/");
        project.Build.Psr4Includes.Should().ContainSingle("extra/");
    }

    [Fact]
    public void Project_ParsesCheckerSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["checker:maxFixIterations"] = "3",
                ["checker:templateStringMaxStates"] = "64",
            })
            .Build();

        var project = new Project(configuration);

        project.Checker.MaxFixIterations.Should().Be(3);
        project.Checker.TemplateStringMaxStates.Should().Be(64);
    }

    [Fact]
    public void Project_CliMaxFixIterations_OverlaysCheckerSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["checker:maxFixIterations"] = "3",
                ["max-fix-iterations"] = "7",
            })
            .Build();

        var project = new Project(configuration);

        project.Checker.MaxFixIterations.Should().Be(7);
    }

    [Fact]
    public void Project_ParsesTyhpdefIncludeExclude()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["tyhpdefInclude:0"] = "defs/**/*.tyhpdef",
                ["tyhpdefExclude:0"] = "defs/generated/**",
            })
            .Build();

        var project = new Project(configuration);

        project.TyhpdefOptions.Include.Should().ContainSingle("defs/**/*.tyhpdef");
        project.TyhpdefOptions.Exclude.Should().ContainSingle("defs/generated/**");
        project.TyhpdefIncludePaths.Should().BeEquivalentTo(project.TyhpdefOptions.Include);
        project.TyhpdefExcludePaths.Should().BeEquivalentTo(project.TyhpdefOptions.Exclude);
    }

    [Fact]
    public void Project_IncludePromotesPackageManifestAndTyhpdefPatterns()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["include:0"] = "./src/**/*.tyhp",
                ["include:1"] = "./runtime/packages/core/package.tyhp.json",
                ["include:2"] = "./ext/**/*.tyhpdef",
            })
            .Build();

        var project = new Project(configuration);

        project.TyhpdefIncludePaths.Should().Contain("./runtime/packages/core/package.tyhp.json");
        project.TyhpdefIncludePaths.Should().Contain("./ext/**/*.tyhpdef");
        project.TyhpdefIncludePaths.Should().NotContain("./src/**/*.tyhp");
    }

    [Fact]
    public void Project_CliFlags_OverlayBuildOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["clean"] = "true",
                ["verbose"] = "true",
                ["dry-run"] = "true",
                ["strict"] = "true",
            })
            .Build();

        var project = new Project(configuration);

        project.Build.CleanBeforeBuild.Should().BeTrue();
        project.Build.Verbose.Should().BeTrue();
        project.Build.DryRun.Should().BeTrue();
        project.Build.StrictMode.Should().BeTrue();

        // Project-level pass-throughs expose the same Story 10 BuildConfig values.
        project.Clean.Should().BeTrue();
        project.Verbose.Should().BeTrue();
        project.DryRun.Should().BeTrue();
        project.Strict.Should().BeTrue();
    }

    [Fact]
    public void Project_JsonOutput_ReadsJsonCliFlag()
    {
        var enabled = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["json"] = "true",
            })
            .Build());

        enabled.JsonOutput.Should().BeTrue();

        var disabled = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["json"] = "false",
            })
            .Build());

        disabled.JsonOutput.Should().BeFalse();

        var absent = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build());

        absent.JsonOutput.Should().BeFalse();
    }

    [Fact]
    public void Project_InvalidPhpVersion_FallsBackToDefault()
    {
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "5.6",
                ["quiet"] = "true",
            })
            .Build());

        project.Output.PhpVersion.Should().Be("8.4");
    }

    [Fact]
    public void Project_InvalidProjectType_DefaultsToApplication()
    {
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["type"] = "invalid_type",
                ["quiet"] = "true",
            })
            .Build());

        project.Type.Should().Be(ProjectType.Application);
        project.Build.GenerateTyhpdef.Should().BeFalse();
    }

    [Fact]
    public void Project_OptimizeEnableCli_MergesIntoOptimizations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["optimize-enable"] = "constantFolding,extensionOperatorInlining",
                ["build:optimizations:constantFolding"] = "false",
            })
            .Build();

        var project = new Project(configuration);

        project.Build.Optimizations.Should().NotBeNull();
        project.Build.Optimizations!["constantFolding"].Should().BeTrue();
        project.Build.Optimizations!["extensionOperatorInlining"].Should().BeTrue();
    }

    [Fact]
    public void Project_PreservesLegacyPhpVersionTopLevelKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["phpVersion"] = "8.3",
            })
            .Build();

        var project = new Project(configuration);

        project.PhpVersion.Should().Be("8.3");
        project.Output.PhpVersion.Should().Be("8.3");
    }

    [Fact]
    public void Project_ParsesEntryPointAutoloader()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["build:entryPointAutoloader:composer"] = "vendor/autoload.php",
            })
            .Build();

        var project = new Project(configuration);

        project.Build.EntryPointAutoloader.Should().NotBeNull();
        project.Build.EntryPointAutoloader!["composer"].Should().Be("vendor/autoload.php");
    }

    [Fact]
    public void Project_ParsesNestedJsonFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-config-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var jsonPath = Path.Combine(tempDir, "tyhp.json");
        File.WriteAllText(jsonPath, """
            {
                "type": "library",
                "output": { "path": "out/", "phpVersion": "8.4" },
                "build": { "generateTyhpdef": false },
                "checker": { "maxFixIterations": 5 }
            }
            """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(tempDir)
                .AddJsonFile("tyhp.json")
                .Build();
            var project = new Project(configuration);

            project.Type.Should().Be(ProjectType.Library);
            project.Output.Path.Should().Be("out/");
            project.Build.GenerateTyhpdef.Should().BeFalse();
            project.Checker.MaxFixIterations.Should().Be(5);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Project_ReadsIncludeAndExcludeArrays()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["include:0"] = "src/**/*.tyhp",
                ["include:1"] = "lib/**/*.tyhp",
                ["exclude:0"] = "vendor/**",
            })
            .Build();

        var project = new Project(configuration);

        project.IncludePaths.Should().Equal("src/**/*.tyhp", "lib/**/*.tyhp");
        project.ExcludePaths.Should().Equal("vendor/**");
    }

    [Fact]
    public void Project_ReadsIncludeAndExcludeFromCommandLineFlags()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(["--include=src/**/*.tyhp", "--exclude=vendor/**"])
            .Build();

        var project = new Project(configuration);

        project.IncludePaths.Should().Equal("src/**/*.tyhp");
        project.ExcludePaths.Should().Equal("vendor/**");
    }

    [Fact]
    public void Project_SplitsCommaSeparatedCommandLineGlobs()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(["--include=src/**/*.tyhp, lib/**/*.tyhp"])
            .Build();

        var project = new Project(configuration);

        project.IncludePaths.Should().Equal("src/**/*.tyhp", "lib/**/*.tyhp");
    }

    [Fact]
    public void Project_CommandLineIncludeReplacesConfigFileArray()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["include:0"] = "src/**/*.tyhp",
                ["include:1"] = "lib/**/*.tyhp",
            })
            .AddCommandLine(["--include=only/**/*.tyhp"])
            .Build();

        var project = new Project(configuration);

        project.IncludePaths.Should().Equal("only/**/*.tyhp");
    }
}
