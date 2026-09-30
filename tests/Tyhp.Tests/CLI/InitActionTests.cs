using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class InitActionTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public void Init_ScaffoldsProjectStructureAndConfig()
    {
        var target = this.CreateTempDirectory();

        RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        File.Exists(Path.Combine(target, "tyhp.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "src")).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "build")).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "tyhpdef")).Should().BeTrue();

        var indexPath = Path.Combine(target, "src", "index.tyhp");
        File.Exists(indexPath).Should().BeTrue();
        File.ReadAllText(indexPath).Should().StartWith("<?tyhp");
    }

    [Fact]
    public void Init_GeneratedConfig_IsReadableByProject()
    {
        var target = this.CreateTempDirectory();

        RunInit(target);

        var configPath = Path.Combine(target, "tyhp.json");
        var project = new Project(new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = configPath,
                ["quiet"] = "true",
            })
            .Build());

        project.IncludePaths.Should().Equal("src/**/*.tyhp");
        project.ExcludePaths.Should().Equal("vendor/**", "node_modules/**");
        project.TyhpdefIncludePaths.Should().Equal("./vendor-tyhpdef/**/*.tyhpdef");
        project.TyhpdefOverlayPaths.Should().Equal("./tyhpdef/**/*.tyhpdef");
        project.Tagless.Should().BeFalse();
        project.Output.Path.Should().Be("build/");
        project.Output.PhpVersion.Should().Be("8.4");
        project.Output.StrictTypes.Should().BeTrue();
        project.Output.IncludeComments.Should().BeTrue();
    }

    [Fact]
    public void Init_WithoutNamespaceFlag_OmitsPsr4()
    {
        var target = this.CreateTempDirectory();

        RunInit(target);

        ReadConfig(target).RootElement.TryGetProperty("psr4", out _).Should().BeFalse();
    }

    [Fact]
    public void Init_WithNamespaceFlag_MapsPrefixToSourceDirectory()
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?> { ["namespace"] = @"Acme\Web" });

        var psr4 = ReadConfig(target).RootElement.GetProperty("psr4");
        psr4.GetProperty(@"Acme\Web\").GetString().Should().Be("src/");
        File.ReadAllText(Path.Combine(target, "src", "index.tyhp"))
            .Should().Contain(@"namespace Acme\Web;");
    }

    [Fact]
    public void Init_ExistingConfig_FailsWithoutOverwriting()
    {
        var target = this.CreateTempDirectory();
        var configPath = Path.Combine(target, "tyhp.json");
        File.WriteAllText(configPath, "{ \"mine\": true }");

        RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        File.ReadAllText(configPath).Should().Be("{ \"mine\": true }");
        Directory.Exists(Path.Combine(target, "src")).Should().BeFalse();
    }

    [Fact]
    public void Init_ExistingScaffoldFile_IsNotOverwritten()
    {
        var target = this.CreateTempDirectory();
        var indexPath = Path.Combine(target, "src", "index.tyhp");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.WriteAllText(indexPath, "existing user code");

        RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        File.ReadAllText(indexPath).Should().Be("existing user code");
    }

    [Fact]
    public void Init_TargetIsFile_Fails()
    {
        var parent = this.CreateTempDirectory();
        var filePath = Path.Combine(parent, "not-a-directory");
        File.WriteAllText(filePath, "");

        RunInit(filePath);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        File.ReadAllText(filePath).Should().BeEmpty();
    }

    [Fact]
    public void Init_UnknownTemplate_FailsAndCreatesNothing()
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?> { ["template"] = "not-a-template" });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        Directory.GetFileSystemEntries(target).Should().BeEmpty();
    }

    [Fact]
    public void Init_AppendsOutputDirectoryToExistingGitignore()
    {
        var target = this.CreateTempDirectory();
        var gitignorePath = Path.Combine(target, ".gitignore");
        File.WriteAllText(gitignorePath, "vendor/\n");

        RunInit(target, new Dictionary<string, string?> { ["output"] = "dist" });

        File.ReadAllLines(gitignorePath)
            .Should().Equal("vendor/", "dist/", "tyhp.pid", ".tyhp-cache/", "vendor-tyhpdef/");
    }

    [Fact]
    public void Init_DoesNotDuplicateExistingGitignoreEntries()
    {
        var target = this.CreateTempDirectory();
        var gitignorePath = Path.Combine(target, ".gitignore");
        File.WriteAllText(gitignorePath, "build/\ntyhp.pid\n.tyhp-cache/\n");

        RunInit(target);

        File.ReadAllLines(gitignorePath)
            .Should().Equal("build/", "tyhp.pid", ".tyhp-cache/", "vendor-tyhpdef/");
    }

    [Fact]
    public void Init_MissingGitignore_IsNotCreated()
    {
        var target = this.CreateTempDirectory();

        RunInit(target);

        File.Exists(Path.Combine(target, ".gitignore")).Should().BeFalse();
    }

    [Theory]
    [InlineData("src", "/absolute/path")]
    [InlineData("src", "../outside")]
    [InlineData("output", "/absolute/path")]
    [InlineData("output", "../outside")]
    public void Init_DirectoryOptionOutsideProject_Fails(string option, string value)
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?> { [option] = value });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        Directory.GetFileSystemEntries(target).Should().BeEmpty();
    }

    [Fact]
    public void Init_UnsupportedPhpVersion_Fails()
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?> { ["php-version"] = "9.9" });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        Directory.GetFileSystemEntries(target).Should().BeEmpty();
    }

    [Fact]
    public void Init_InvalidNamespace_Fails()
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?> { ["namespace"] = @"9Bad\Name" });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        Directory.GetFileSystemEntries(target).Should().BeEmpty();
    }

    [Fact]
    public void Init_CustomDirectories_AreReflectedInConfigAndScaffold()
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?>
        {
            ["src"] = "./lib",
            ["output"] = "./out",
            ["php-version"] = "8.2",
        });

        var root = ReadConfig(target).RootElement;
        root.GetProperty("include")[0].GetString().Should().Be("lib/**/*.tyhp");
        root.GetProperty("output").GetProperty("path").GetString().Should().Be("out/");
        root.GetProperty("output").GetProperty("phpVersion").GetString().Should().Be("8.2");
        File.Exists(Path.Combine(target, "lib", "index.tyhp")).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "out")).Should().BeTrue();

        var composer = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "composer.json"))).RootElement;
        composer.GetProperty("require").GetProperty("php").GetString().Should().Be("~8.2.0");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        composer.GetProperty("require").TryGetProperty("tyhpdef/php", out _).Should().BeFalse();
        composer.GetProperty("require-dev").GetProperty("tyhpdef/php").GetString().Should().Be("@dev");
        composer.GetProperty("require-dev").GetProperty("tyhp/compiler").GetString()
            .Should().Be(ComposerJsonService.ResolveCompilerPackageVersion());
    }

    [Fact]
    public void Init_GreenfieldComposerJson_HasShapeAScriptsAndDevCompiler()
    {
        var target = this.CreateTempDirectory();

        RunInit(target);

        var composer = ReadComposer(target);
        composer.GetProperty("require-dev").GetProperty("tyhpdef/php").GetString().Should().Be("@dev");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        composer.GetProperty("require").TryGetProperty("tyhpdef/php", out _).Should().BeFalse();
        composer.GetProperty("require-dev").GetProperty("tyhpdef/php").GetString()
            .Should().NotBe(ComposerJsonService.EncodeRuntimePackageVersion("8.4", RuntimePackageVersions.Php));
        composer.GetProperty("require-dev").GetProperty("tyhp/compiler").GetString()
            .Should().Be(ComposerJsonService.ResolveCompilerPackageVersion());

        var scripts = composer.GetProperty("scripts");
        scripts.GetProperty("tyhp").GetString().Should().Be("vendor/bin/tyhp");
        var dump = scripts.GetProperty("post-autoload-dump").EnumerateArray().Select(e => e.GetString()).ToList();
        dump.Should().Equal(
            "vendor/bin/tyhp --install-binary",
            "vendor/bin/tyhp generate_tyhpdef --vendor");
        composer.GetProperty("scripts-descriptions").GetProperty("tyhp").GetString()
            .Should().Be(InitComposerManifest.TyhpScriptDescription);
        composer.GetProperty("minimum-stability").GetString().Should().Be("alpha");
        composer.GetProperty("prefer-stable").GetBoolean().Should().BeTrue();
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Init_GreenfieldTyhpJsonAndGitignore_IncludeVendorTyhpdef()
    {
        var target = this.CreateTempDirectory();
        var gitignorePath = Path.Combine(target, ".gitignore");
        File.WriteAllText(gitignorePath, "vendor/\n");

        RunInit(target);

        var root = ReadConfig(target).RootElement;
        root.GetProperty("tyhpdefInclude")[0].GetString().Should().Be("./vendor-tyhpdef/**/*.tyhpdef");
        root.GetProperty("overlay")[0].GetString().Should().Be("./tyhpdef/**/*.tyhpdef");
        File.ReadAllLines(gitignorePath).Should().Contain("vendor-tyhpdef/");
        Directory.Exists(Path.Combine(target, "vendor")).Should().BeFalse();
    }

    [Fact]
    public void Init_ExistingComposerJson_MergesMissingKeysWithoutClobbering()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "name": "acme/existing",
                "license": "MIT",
                "autoload": {
                    "files": ["src/helpers.php"]
                },
                "require": {
                    "php": ">=8.1",
                    "tyhpdef/php": "^1.2",
                    "acme/logger": "^3.0"
                },
                "scripts": {
                    "tyhp": "php vendor/bin/custom-tyhp",
                    "test": "phpunit"
                }
            }
            """);

        RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        var composer = ReadComposer(target);
        composer.GetProperty("name").GetString().Should().Be("acme/existing");
        composer.GetProperty("license").GetString().Should().Be("MIT");
        composer.GetProperty("autoload").GetProperty("files")[0].GetString()
            .Should().Be("src/helpers.php");
        composer.GetProperty("require").GetProperty("php").GetString().Should().Be(">=8.1");
        File.ReadAllText(Path.Combine(target, "composer.json"))
            .Should().Contain(">=8.1")
            .And.NotContain("\\u003E");
        composer.GetProperty("require").GetProperty("tyhpdef/php").GetString().Should().Be("^1.2");
        composer.GetProperty("require-dev").TryGetProperty("tyhpdef/php", out _).Should().BeFalse();
        composer.GetProperty("require").GetProperty("acme/logger").GetString().Should().Be("^3.0");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        composer.GetProperty("require-dev").GetProperty("tyhp/compiler").GetString()
            .Should().Be(ComposerJsonService.ResolveCompilerPackageVersion());
        composer.GetProperty("scripts").GetProperty("tyhp").GetString()
            .Should().Be("php vendor/bin/custom-tyhp");
        composer.GetProperty("scripts").GetProperty("test").GetString().Should().Be("phpunit");
        var dump = composer.GetProperty("scripts").GetProperty("post-autoload-dump")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        dump.Should().Equal(
            "vendor/bin/tyhp --install-binary",
            "vendor/bin/tyhp generate_tyhpdef --vendor");
        composer.GetProperty("scripts-descriptions").GetProperty("tyhp").GetString()
            .Should().Be(InitComposerManifest.TyhpScriptDescription);
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
        File.Exists(Path.Combine(target, "tyhp.json")).Should().BeTrue();
    }

    [Fact]
    public void Init_ExistingComposerJson_NamedCompiler_DoesNotSelfRequire()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "name": "tyhp/compiler",
                "require": {
                    "php": ">=8.2"
                }
            }
            """);

        RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        var composer = ReadComposer(target);
        composer.GetProperty("name").GetString().Should().Be("tyhp/compiler");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        composer.GetProperty("require-dev").GetProperty("tyhpdef/php").GetString().Should().Be("@dev");
        composer.GetProperty("require-dev").TryGetProperty("tyhp/compiler", out _).Should().BeFalse();
        composer.GetProperty("require").TryGetProperty("tyhp/compiler", out _).Should().BeFalse();
    }

    [Fact]
    public void Init_WritesAllowPluginsForTyhpCore()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "name": "acme/existing",
                "config": {
                    "allow-plugins": {
                        "other/plugin": true
                    }
                }
            }
            """);

        RunInit(target);

        var composer = ReadComposer(target);
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("other/plugin")
            .GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Init_DoesNotClobberExistingAllowPluginsFalse()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "config": {
                    "allow-plugins": {
                        "tyhp/core": false
                    }
                }
            }
            """);

        RunInit(target);

        ReadComposer(target).GetProperty("config").GetProperty("allow-plugins")
            .GetProperty("tyhp/core").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Init_ExistingComposerJson_AppendsMissingDumpCommands()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "require": {
                    "php": "~8.4.0"
                },
                "scripts": {
                    "post-autoload-dump": [
                        "echo keep-me",
                        "vendor/bin/tyhp --install-binary"
                    ]
                }
            }
            """);

        RunInit(target);

        var composer = ReadComposer(target);
        var dump = composer.GetProperty("scripts").GetProperty("post-autoload-dump")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        dump.Should().Equal(
            "echo keep-me",
            "vendor/bin/tyhp --install-binary",
            "vendor/bin/tyhp generate_tyhpdef --vendor");
        composer.GetProperty("scripts").GetProperty("tyhp").GetString()
            .Should().Be("vendor/bin/tyhp");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        composer.GetProperty("require").TryGetProperty("tyhpdef/php", out _).Should().BeFalse();
        composer.GetProperty("require-dev").GetProperty("tyhpdef/php").GetString().Should().Be("@dev");
    }

    [Fact]
    public void Init_ExistingComposerJson_ConvertsDumpStringToArrayAndAppends()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "scripts": {
                    "post-autoload-dump": "echo keep-me"
                }
            }
            """);

        RunInit(target);

        var dump = ReadComposer(target).GetProperty("scripts").GetProperty("post-autoload-dump")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        dump.Should().Equal(
            "echo keep-me",
            "vendor/bin/tyhp --install-binary",
            "vendor/bin/tyhp generate_tyhpdef --vendor");
    }

    [Fact]
    public void Init_ExistingComposerJson_RewritesScriptPathsForCustomBinDir()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "config": {
                    "bin-dir": "bin"
                }
            }
            """);

        RunInit(target);

        var composer = ReadComposer(target);
        composer.GetProperty("scripts").GetProperty("tyhp").GetString().Should().Be("bin/tyhp");
        var dump = composer.GetProperty("scripts").GetProperty("post-autoload-dump")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        dump.Should().Equal(
            "bin/tyhp --install-binary",
            "bin/tyhp generate_tyhpdef --vendor");
        composer.GetProperty("config").GetProperty("bin-dir").GetString().Should().Be("bin");
    }

    [Fact]
    public void Init_InvalidComposerJson_FailsWithoutWriting()
    {
        var target = this.CreateTempDirectory();
        var composerPath = Path.Combine(target, "composer.json");
        File.WriteAllText(composerPath, "{ not json");

        var action = RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InitComposerJsonInvalid);
        File.ReadAllText(composerPath).Should().Be("{ not json");
        File.Exists(Path.Combine(target, "tyhp.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(target, "src")).Should().BeFalse();
    }

    [Fact]
    public void Init_ComposerJsonArrayRoot_FailsWithoutWriting()
    {
        var target = this.CreateTempDirectory();
        var composerPath = Path.Combine(target, "composer.json");
        File.WriteAllText(composerPath, "[]");

        var action = RunInit(target);

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        action.LastError.Should().Be(MessageCode.InitComposerJsonNotObject);
        File.ReadAllText(composerPath).Should().Be("[]");
        File.Exists(Path.Combine(target, "tyhp.json")).Should().BeFalse();
    }

    [Fact]
    public void Init_YesFlag_DoesNotHangAndStillAbortsWhenTyhpJsonExists()
    {
        var target = this.CreateTempDirectory();

        RunInit(target, new Dictionary<string, string?> { ["yes"] = "true", ["quiet"] = "false" });

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        File.Exists(Path.Combine(target, "tyhp.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, "composer.json")).Should().BeTrue();

        var blocked = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(blocked, "tyhp.json"), "{ \"mine\": true }");
        RunInit(blocked, new Dictionary<string, string?> { ["yes"] = "true", ["quiet"] = "false" });

        Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        File.ReadAllText(Path.Combine(blocked, "tyhp.json")).Should().Be("{ \"mine\": true }");
        File.Exists(Path.Combine(blocked, "composer.json")).Should().BeFalse();
    }

    [Fact]
    public void Init_PhpConstraintThatCannotSatisfyChosenVersion_IsLeftAndDoesNotFail()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "require": {
                    "php": "~8.2.0"
                }
            }
            """);

        RunInit(target, new Dictionary<string, string?> { ["php-version"] = "8.4" });

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        ReadComposer(target).GetProperty("require").GetProperty("php").GetString()
            .Should().Be("~8.2.0");
        File.Exists(Path.Combine(target, "tyhp.json")).Should().BeTrue();
    }

    [Fact]
    public void Init_ExistingComposerJson_BlankPhpConstraintIsTreatedAsMissing()
    {
        var target = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(target, "composer.json"), """
            {
                "require": {
                    "php": ""
                }
            }
            """);

        RunInit(target, new Dictionary<string, string?> { ["php-version"] = "8.4" });

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        ReadComposer(target).GetProperty("require").GetProperty("php").GetString()
            .Should().Be("~8.4.0");
    }

    private static InitAction RunInit(string targetDirectory, Dictionary<string, string?>? options = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["path:0"] = targetDirectory,
            // Quiet keeps the action non-interactive and off the test console.
            ["quiet"] = "true",
        };

        foreach (var (key, value) in options ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build());

        Environment.ExitCode = (int)ExitCode.Success;
        var action = new InitAction(project);
        action.Start(CancellationToken.None);
        return action;
    }

    private static JsonDocument ReadConfig(string targetDirectory)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(targetDirectory, "tyhp.json")));

    private static JsonElement ReadComposer(string targetDirectory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(targetDirectory, "composer.json")));
        return document.RootElement.Clone();
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-init-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        this._tempDirectories.Add(tempDir);
        return tempDir;
    }

    public void Dispose()
    {
        foreach (var directory in this._tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Leftover temp directories are harmless.
            }
        }

        GC.SuppressFinalize(this);
    }
}
