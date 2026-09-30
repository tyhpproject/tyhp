using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

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
        project.Output.PublishPath.Should().Be(".");
        project.Output.PublishClean.Should().BeFalse();
        project.Output.PublishContent.Should().BeEmpty();
        project.Output.PhpVersion.Should().Be("8.2");
        project.Output.PhpVersionWasDefaulted.Should().BeTrue();
        project.Output.StrictTypes.Should().BeTrue();
        project.Output.IncludeComments.Should().BeTrue();
        project.Build.GenerateTyhpdef.Should().BeFalse();
        project.Build.CleanBeforeBuild.Should().BeFalse();
        project.Build.StrictMode.Should().BeFalse();
        project.Checker.MaxFixIterations.Should().Be(10);
        project.Checker.TemplateStringMaxStates.Should().Be(256);
        project.Tagless.Should().BeFalse();
        project.PhpVersion.Should().Be("8.2");
        project.PhpVersionWasDefaulted.Should().BeTrue();
        project.PidFile.Should().BeNull();
        project.SuppressedWarnings.Should().BeEmpty();
    }

    [Fact]
    public void Project_ParsesPidFileFromCliFlag()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["pid-file"] = "/tmp/tyhp.pid",
            })
            .Build();

        new Project(configuration).PidFile.Should().Be("/tmp/tyhp.pid");
    }

    [Fact]
    public void Project_TreatsBlankPidFileAsUnset()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["pid-file"] = "   ",
            })
            .Build();

        new Project(configuration).PidFile.Should().BeNull();
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
                ["output:publishPath"] = "artifacts/",
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
        project.Output.PublishPath.Should().Be("artifacts/");
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
    public void Project_BlankPublishPath_DefaultsToProjectRoot()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:publishPath"] = "  ",
            })
            .Build();

        new Project(configuration).Output.PublishPath.Should().Be(".");
    }

    [Fact]
    public void Project_ParsesPublishCleanAndPublishContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-config-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var jsonPath = Path.Combine(tempDir, "tyhp.json");
        File.WriteAllText(jsonPath, """
            {
                "output": {
                    "publishPath": "publish/",
                    "publishClean": true,
                    "publishContent": [
                        { "src": "README.md" },
                        {
                            "src": ["docs/**/*.md", "notes/*.txt"],
                            "dst": "docs/",
                            "exclude": ["docs/drafts/**"],
                            "flat": true,
                            "preserve": "always",
                            "skipUnchanged": false,
                            "overwriteReadOnly": false,
                            "required": true,
                            "followSymlinks": true
                        }
                    ]
                }
            }
            """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(tempDir)
                .AddJsonFile("tyhp.json")
                .Build();
            var project = new Project(configuration);

            project.Output.PublishPath.Should().Be("publish/");
            project.Output.PublishClean.Should().BeTrue();
            project.Output.PublishContent.Should().HaveCount(2);

            var readme = project.Output.PublishContent[0];
            readme.Src.Should().Equal("README.md");
            readme.Dst.Should().Be(".");
            readme.Flat.Should().BeFalse();
            readme.Preserve.Should().Be(PublishContentPreserve.Newest);
            readme.SkipUnchanged.Should().BeTrue();
            readme.OverwriteReadOnly.Should().BeTrue();
            readme.Required.Should().BeFalse();
            readme.FollowSymlinks.Should().BeFalse();

            var docs = project.Output.PublishContent[1];
            docs.Src.Should().Equal("docs/**/*.md", "notes/*.txt");
            docs.Dst.Should().Be("docs");
            docs.DstForcedDirectory.Should().BeTrue();
            docs.Exclude.Should().Equal("docs/drafts/**");
            docs.Flat.Should().BeTrue();
            docs.Preserve.Should().Be(PublishContentPreserve.Always);
            docs.SkipUnchanged.Should().BeFalse();
            docs.OverwriteReadOnly.Should().BeFalse();
            docs.Required.Should().BeTrue();
            docs.FollowSymlinks.Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Project_InvalidPublishPreserve_DefaultsToNewest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:publishContent:0:src"] = "README.md",
                ["output:publishContent:0:preserve"] = "sometimes",
                ["quiet"] = "true",
            })
            .Build();

        var project = new Project(configuration);

        project.Output.PublishContent.Should().ContainSingle();
        project.Output.PublishContent[0].Preserve.Should().Be(PublishContentPreserve.Newest);
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
                ["include:1"] = "./pkg/composer.json",
                ["include:2"] = "./ext/**/*.tyhpdef",
            })
            .Build();

        var project = new Project(configuration);

        project.TyhpdefIncludePaths.Should().Contain("./pkg/composer.json");
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
    public void Project_BuildStrictMode_FromTyhpJson()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["build:strictMode"] = "true",
            })
            .Build();

        new Project(configuration).Build.StrictMode.Should().BeTrue();
    }

    [Fact]
    public void Project_CliStrict_WinsOverTyhpJson()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["build:strictMode"] = "false",
                ["strict"] = "true",
            })
            .Build();

        new Project(configuration).Build.StrictMode.Should().BeTrue();
    }

    [Fact]
    public void Project_CliFix_SetsBuildFixNotFromLintFix()
    {
        var fromCli = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["fix"] = "true",
            })
            .Build());

        fromCli.Build.Fix.Should().BeTrue();

        var fromLintOnly = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["lint:fix"] = "true",
            })
            .Build());

        fromLintOnly.Build.Fix.Should().BeFalse();
        fromLintOnly.LintFix.Should().BeTrue();
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

    [Fact]
    public void Project_ParsesXDebugProxySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["xdebugProxy:idePort"] = "9010",
                ["xdebugProxy:xdebugPort"] = "9011",
                ["xdebugProxy:ideListenAddress"] = "0.0.0.0",
                ["xdebugProxy:xdebugListenAddress"] = "127.0.0.1",
                ["xdebugProxy:sourceMapDir"] = "maps/",
                ["xdebugProxy:ideKey"] = "TYHP",
                ["xdebugProxy:maxSessions"] = "3",
                ["xdebugProxy:logLevel"] = "debug",
                ["xdebugProxy:autoReloadSourceMaps"] = "false",
            })
            .Build();

        var project = new Project(configuration);

        project.XDebugProxy.IdeListenPort.Should().Be(9010);
        project.XDebugProxy.XDebugListenPort.Should().Be(9011);
        project.XDebugProxy.IdeListenAddress.Should().Be("0.0.0.0");
        project.XDebugProxy.XDebugListenAddress.Should().Be("127.0.0.1");
        project.XDebugProxy.SourceMapDirectory.Should().Be("maps/");
        project.XDebugProxy.IdeKey.Should().Be("TYHP");
        project.XDebugProxy.MaxSessions.Should().Be(3);
        project.XDebugProxy.LogLevel.Should().Be("debug");
        project.XDebugProxy.AutoReloadSourceMaps.Should().BeFalse();
    }

    [Fact]
    public void Project_CliIdePort_OverridesXDebugProxySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["xdebugProxy:idePort"] = "9003",
                ["xdebugProxy:xdebugPort"] = "9004",
                ["xdebugProxy:sourceMapDir"] = "from-json/",
                ["xdebugProxy:ideKey"] = "JSON",
                ["xdebugProxy:logLevel"] = "info",
                ["ide-port"] = "9005",
                ["xdebug-port"] = "9006",
                ["sourcemap-dir"] = "from-cli/",
                ["ide-key"] = "CLI",
                ["log-level"] = "debug",
            })
            .Build();

        var project = new Project(configuration);

        project.XDebugProxy.IdeListenPort.Should().Be(9005);
        project.XDebugProxy.XDebugListenPort.Should().Be(9006);
        project.XDebugProxy.SourceMapDirectory.Should().Be("from-cli/");
        project.XDebugProxy.IdeKey.Should().Be("CLI");
        project.XDebugProxy.LogLevel.Should().Be("debug");
    }

    [Fact]
    public void TyhpdefGetters_DefaultOutputDirIsCwdTyhpdefWhenNoProjectFile()
    {
        var project = new Project(new ConfigurationBuilder().Build());

        project.GetTyhpdefOutputDir().Should().Be(Path.Combine(Directory.GetCurrentDirectory(), "tyhpdef"));
        project.GetTyhpdefOutputFile().Should().BeNull();
        project.GetPhpExecutablePath().Should().BeNull();
        project.GetTyhpdefPhpVersion().Should().BeNull();
        project.GetTyhpdefPhpTargets().Should().BeEmpty();
        project.GetTyhpdefSourcePaths().Should().BeEmpty();
        project.GetTyhpdefPackagePath().Should().BeNull();
        project.GetTyhpdefNoDocs().Should().BeFalse();
        project.GetTyhpdefIncludeInternal().Should().BeFalse();
        project.GetTyhpdefIncludeDeprecated().Should().BeTrue();
        project.GetTyhpdefOverwrite().Should().BeFalse();
        project.GetTyhpdefNoPhp().Should().BeFalse();
        project.GetTyhpdefVerify().Should().BeFalse();
        project.GetTyhpdefSplit().Should().Be("file");
        project.GetTyhpdefIncludeDev().Should().BeFalse();
        project.GetTyhpdefRequireStubs().Should().BeFalse();
        project.GetTyhpdefValidatePath().Should().BeNull();
        project.GetTyhpdefAuditStubsPath().Should().BeNull();
        project.GetTyhpdefAuditOutPath().Should().BeNull();
        project.GetTyhpdefLocale().Should().Be(project.Locale);
    }

    [Fact]
    public void TyhpdefGetters_ReadCliFlags()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "tyhp-tyhpdef-out");
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output"] = outputDir,
                ["output-file"] = "ExtWidget.tyhpdef",
                ["php"] = "/usr/bin/php",
                ["php-version"] = "8.3",
                ["php-targets"] = "8.2, 8.3,8.4",
                ["php-runtime-dir"] = "/tmp/php-runtimes",
                ["source"] = "./src/**/*.php,./lib/*.php",
                ["package-path"] = "./vendor/acme/http",
                ["no-docs"] = "true",
                ["include-internal"] = "true",
                ["no-deprecated"] = "true",
                ["overwrite"] = "true",
                ["no-php"] = "true",
                ["validate"] = "./tyhpdef",
                ["audit-stubs"] = "./_tyhpdef",
                ["out"] = "stub-audit.md",
                ["verify"] = "true",
                ["split"] = "type",
                ["include-dev"] = "true",
                ["require-stubs"] = "true",
                ["no-php-runtime-update"] = "true",
                ["refresh-snapshots"] = "true",
            })
            .Build());

        project.GetTyhpdefOutputDir().Should().Be(Path.GetFullPath(outputDir));
        project.GetTyhpdefOutputFile().Should().Be("ExtWidget.tyhpdef");
        project.GetPhpExecutablePath().Should().Be("/usr/bin/php");
        project.GetTyhpdefPhpVersion().Should().Be("8.3");
        project.GetTyhpdefPhpTargets().Should().Equal("8.2", "8.3", "8.4");
        project.GetTyhpdefPhpRuntimeDir().Should().Be("/tmp/php-runtimes");
        project.GetTyhpdefSourcePaths().Should().Equal("./src/**/*.php", "./lib/*.php");
        project.GetTyhpdefPackagePath().Should().Be("./vendor/acme/http");
        project.GetTyhpdefNoDocs().Should().BeTrue();
        project.GetTyhpdefIncludeInternal().Should().BeTrue();
        project.GetTyhpdefIncludeDeprecated().Should().BeFalse();
        project.GetTyhpdefOverwrite().Should().BeTrue();
        project.GetTyhpdefNoPhp().Should().BeTrue();
        project.GetTyhpdefValidatePath().Should().Be("./tyhpdef");
        project.GetTyhpdefAuditStubsPath().Should().Be("./_tyhpdef");
        project.GetTyhpdefAuditOutPath().Should().Be("stub-audit.md");
        project.GetTyhpdefVerify().Should().BeTrue();
        project.GetTyhpdefSplit().Should().Be("type");
        project.GetTyhpdefIncludeDev().Should().BeTrue();
        project.GetTyhpdefRequireStubs().Should().BeTrue();
        project.GetTyhpdefNoPhpRuntimeUpdate().Should().BeTrue();
        project.GetTyhpdefRefreshSnapshots().Should().BeTrue();
    }

    [Fact]
    public void Project_RepeatedSourceFlagsAccumulateLikeACommaSeparatedList()
    {
        var repeated = ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--output=/tmp/tyhp-out",
            "--source=./src/A.php",
            "--no-docs=true",
            "--source",
            "./lib/B.php",
        ]);
        var commaSeparated = new[]
        {
            "--output=/tmp/tyhp-out",
            "--source=./src/A.php,./lib/B.php",
            "--no-docs=true",
        };

        var fromRepeated = new Project(new ConfigurationBuilder().AddCommandLine(repeated).Build());
        var fromComma = new Project(new ConfigurationBuilder().AddCommandLine(commaSeparated).Build());

        fromRepeated.GetTyhpdefSourcePaths().Should().Equal(fromComma.GetTyhpdefSourcePaths());
        fromRepeated.GetTyhpdefSourcePaths().Should().Equal("./src/A.php", "./lib/B.php");
        fromRepeated.GetTyhpdefNoDocs().Should().BeTrue();

        var single = new[] { "--output=/tmp/tyhp-out", "--source", "./only.php", "--no-docs=true" };
        ActionConfigProvider.AccumulateRepeatedSourceFlags(single).Should().Equal(single);

        var withComma = ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source=./src/A.php,./lib/B.php",
            "--source",
            "./lib/C.php",
        ]);
        new Project(new ConfigurationBuilder().AddCommandLine(withComma).Build())
            .GetTyhpdefSourcePaths()
            .Should().Equal("./src/A.php", "./lib/B.php", "./lib/C.php");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source=./src/A.php",
            "--source",
            "./lib/B.php",
            "lint",
            "--source",
            "./other.php",
        ]).Should().Equal(
            "--source=./src/A.php,./lib/B.php",
            "lint",
            "--source",
            "./other.php");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source",
            "lint",
            "--source=./other.php",
        ]).Should().Equal("--source=lint,./other.php");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source",
            "build",
            "--source=./lib/B.php",
            "lint",
            "--source",
            "./other.php",
        ]).Should().Equal(
            "--source=build,./lib/B.php",
            "lint",
            "--source",
            "./other.php");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--output",
            "build",
            "--source=./src/A.php",
            "--source",
            "./lib/B.php",
        ]).Should().Equal(
            "--output",
            "build",
            "--source=./src/A.php,./lib/B.php");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source",
            "--lint",
        ]).Should().Equal("--lint");

        var sourceSwitchIsNotAPath = ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source=./src/A.php",
            "--source",
            "--lint",
        ]);
        sourceSwitchIsNotAPath.Should().Equal("--source=./src/A.php", "--lint");
        new Project(new ConfigurationBuilder().AddCommandLine(
                ActionConfigProvider.SelectBinderArgs(sourceSwitchIsNotAPath)).Build())
            .GetTyhpdefSourcePaths().Should().Equal("./src/A.php");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source=lint",
        ]).Should().Equal("--source=lint");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source=./src/A.php",
            "--source",
            "./lib/B.php",
            "--source",
            "--lint",
        ]).Should().Equal("--source=./src/A.php,./lib/B.php", "--lint");

        ActionConfigProvider.AccumulateRepeatedSourceFlags(
        [
            "--source",
            "--lint",
            "--source=./src/A.php",
            "--source",
            "./lib/B.php",
            "lint",
            "--source",
            "./other.php",
        ]).Should().Equal(
            "--lint",
            "--source=./src/A.php,./lib/B.php",
            "lint",
            "--source",
            "./other.php");
    }

    [Fact]
    public void Project_RepeatedIncludeExcludeAndSuppressWarningsAccumulateLikeACommaSeparatedList()
    {
        var repeated = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include=src/**/*.tyhp",
            "--exclude",
            "vendor/**",
            "--suppress-warnings=TYHP8027",
            "--include",
            "lib/**/*.tyhp",
            "--exclude=tests/**",
            "--suppress-warnings",
            "8021",
        ]);

        var project = new Project(new ConfigurationBuilder().AddCommandLine(repeated).Build());
        project.IncludePaths.Should().Equal("src/**/*.tyhp", "lib/**/*.tyhp");
        project.ExcludePaths.Should().Equal("vendor/**", "tests/**");
        project.SuppressedWarnings.Should().BeEquivalentTo(
        [
            MessageCode.TyhpdefRuntimePackageNotFound,
            MessageCode.TyhpdefOverlayStampMismatch,
        ]);

        var single = new[] { "--include", "src/**/*.tyhp", "--exclude=vendor/**", "--suppress-warnings=TYHP8027" };
        ActionConfigProvider.AccumulateRepeatedListFlags(single).Should().Equal(single);

        var withComma = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include=src/**/*.tyhp,lib/**/*.tyhp",
            "--include",
            "tests/**/*.tyhp",
        ]);
        new Project(new ConfigurationBuilder().AddCommandLine(withComma).Build())
            .IncludePaths
            .Should().Equal("src/**/*.tyhp", "lib/**/*.tyhp", "tests/**/*.tyhp");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--exclude=vendor/**",
            "--exclude",
            "build/**",
            "lint",
            "--exclude",
            "other/**",
        ]).Should().Equal(
            "--exclude=vendor/**,build/**",
            "lint",
            "--exclude",
            "other/**");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--suppress-warnings",
            "lint",
            "--suppress-warnings=8021",
        ]).Should().Equal("--suppress-warnings=lint,8021");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include",
            "build",
            "--include=lib/**/*.tyhp",
            "lint",
            "--include",
            "other/**",
        ]).Should().Equal(
            "--include=build,lib/**/*.tyhp",
            "lint",
            "--include",
            "other/**");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--output",
            "build",
            "--exclude=vendor/**",
            "--exclude",
            "tests/**",
        ]).Should().Equal(
            "--output",
            "build",
            "--exclude=vendor/**,tests/**");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include",
            "--lint",
        ]).Should().Equal("--lint");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--suppress-warnings=8021",
        ]).Should().Equal("--suppress-warnings=8021");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include=src/**/*.tyhp",
            "--include",
            "lib/**/*.tyhp",
            "--include",
            "--lint",
        ]).Should().Equal("--include=src/**/*.tyhp,lib/**/*.tyhp", "--lint");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--exclude",
            "--lint",
            "--exclude=vendor/**",
            "--exclude",
            "tests/**",
            "lint",
            "--exclude",
            "other/**",
        ]).Should().Equal(
            "--lint",
            "--exclude=vendor/**,tests/**",
            "lint",
            "--exclude",
            "other/**");

        var sourceOnly = new[]
        {
            "--source=./src/A.php",
            "--source",
            "./lib/B.php",
            "lint",
            "--source",
            "./other.php",
        };
        ActionConfigProvider.AccumulateRepeatedListFlags(sourceOnly)
            .Should().Equal(ActionConfigProvider.AccumulateRepeatedSourceFlags(sourceOnly));

        var switchIsNotAPath = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include",
            "--foo",
        ]);
        switchIsNotAPath.Should().Equal("--foo");
        new Project(new ConfigurationBuilder().AddCommandLine(
                ActionConfigProvider.SelectBinderArgs(switchIsNotAPath)).Build())
            .IncludePaths.Should().BeEmpty();

        var oneValueThenSwitch = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include=src/**/*.tyhp",
            "--include",
            "--foo",
        ]);
        oneValueThenSwitch.Should().Equal("--include=src/**/*.tyhp", "--foo");
        new Project(new ConfigurationBuilder().AddCommandLine(
                ActionConfigProvider.SelectBinderArgs(oneValueThenSwitch)).Build())
            .IncludePaths.Should().Equal("src/**/*.tyhp");

        var trailingBare = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--include=src/**/*.tyhp",
            "--include",
            "lib/**/*.tyhp",
            "--include",
        ]);
        trailingBare.Should().Equal("--include=src/**/*.tyhp,lib/**/*.tyhp");
        new Project(new ConfigurationBuilder().AddCommandLine(
                ActionConfigProvider.SelectBinderArgs(trailingBare)).Build())
            .IncludePaths.Should().Equal("src/**/*.tyhp", "lib/**/*.tyhp");
    }

    [Fact]
    public void Project_RepeatedPhpTargetsAccumulateLikeACommaSeparatedList()
    {
        var repeated = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets=8.2",
            "--php-targets",
            "8.3",
        ]);
        new Project(new ConfigurationBuilder().AddCommandLine(repeated).Build())
            .GetTyhpdefPhpTargets()
            .Should().Equal("8.2", "8.3");

        var single = new[] { "--php-targets", "8.4" };
        ActionConfigProvider.AccumulateRepeatedListFlags(single).Should().Equal(single);

        var withComma = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets=8.2,8.3",
            "--php-targets",
            "8.4",
        ]);
        new Project(new ConfigurationBuilder().AddCommandLine(withComma).Build())
            .GetTyhpdefPhpTargets()
            .Should().Equal("8.2", "8.3", "8.4");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets=8.2",
            "--php-targets",
            "8.3",
            "lint",
            "--php-targets",
            "8.4",
        ]).Should().Equal(
            "--php-targets=8.2,8.3",
            "lint",
            "--php-targets",
            "8.4");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets",
            "lint",
            "--php-targets=8.3",
        ]).Should().Equal("--php-targets=lint,8.3");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets",
            "--lint",
        ]).Should().Equal("--lint");

        var oneValueThenSwitch = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets=8.2",
            "--php-targets",
            "--lint",
        ]);
        oneValueThenSwitch.Should().Equal("--php-targets=8.2", "--lint");
        new Project(new ConfigurationBuilder().AddCommandLine(
                ActionConfigProvider.SelectBinderArgs(oneValueThenSwitch)).Build())
            .GetTyhpdefPhpTargets().Should().Equal("8.2");

        var trailingBare = ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets=8.2",
            "--php-targets",
            "8.3",
            "--php-targets",
        ]);
        trailingBare.Should().Equal("--php-targets=8.2,8.3");
        new Project(new ConfigurationBuilder().AddCommandLine(
                ActionConfigProvider.SelectBinderArgs(trailingBare)).Build())
            .GetTyhpdefPhpTargets().Should().Equal("8.2", "8.3");

        ActionConfigProvider.AccumulateRepeatedListFlags(
        [
            "--php-targets",
            "--lint",
            "--php-targets=8.2",
            "--php-targets",
            "8.3",
            "lint",
            "--php-targets",
            "8.4",
        ]).Should().Equal(
            "--lint",
            "--php-targets=8.2,8.3",
            "lint",
            "--php-targets",
            "8.4");
    }

    [Fact]
    public void GetTyhpdefIncludeDeprecated_NoDeprecatedWinsOverIncludeDeprecated()
    {
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["include-deprecated"] = "true",
                ["no-deprecated"] = "true",
            })
            .Build());

        project.GetTyhpdefIncludeDeprecated().Should().BeFalse();
    }

    [Fact]
    public void Project_ParsesSuppressWarnings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["suppressWarnings:0"] = "TYHP8027",
                ["suppressWarnings:1"] = "8021",
                ["suppressWarnings:2"] = "tyhp8027",
            })
            .Build();

        var project = new Project(configuration);

        project.SuppressedWarnings.Should().BeEquivalentTo(
            new[]
            {
                MessageCode.TyhpdefRuntimePackageNotFound,
                MessageCode.TyhpdefOverlayStampMismatch,
            });
    }

    [Fact]
    public void Project_CliSuppressWarnings_ReplacesJsonArray()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["suppressWarnings:0"] = "TYHP8021",
                ["suppress-warnings"] = "TYHP8027",
            })
            .Build();

        var project = new Project(configuration);

        project.SuppressedWarnings.Should().Equal(MessageCode.TyhpdefRuntimePackageNotFound);
    }

    [Fact]
    public void Project_InvalidSuppressWarningsEntry_EmitsConfigWarning()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["suppressWarnings:0"] = "TYHP8027",
                ["suppressWarnings:1"] = "not-a-code",
            })
            .Build();

        var project = new Project(configuration);
        var bag = new DiagnosticBag();
        project.TransferPendingConfigWarningsTo(bag);

        project.SuppressedWarnings.Should().Equal(MessageCode.TyhpdefRuntimePackageNotFound);
        bag.Warnings.Should().Contain(d => d.Code == MessageCode.ConfigInvalidValue);
    }
}
