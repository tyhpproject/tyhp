using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Enums;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class ComposerActionTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void FilterComposerArgs_StripsNoTyhpdefAndKeepsComposerTokens()
    {
        var filtered = ComposerAction.FilterComposerArgs(
        [
            "require",
            "acme/http",
            "--no-tyhpdef=true",
            "--prefer-dist",
        ]);

        filtered.Should().Equal("require", "acme/http", "--prefer-dist");
    }

    [Fact]
    public void FilterComposerArgs_StripsSpaceSeparatedPidFileValue()
    {
        var filtered = ComposerAction.FilterComposerArgs(
        [
            "install",
            "--pid-file",
            "/tmp/tyhp.pid",
            "--no-dev",
        ]);

        filtered.Should().Equal("install", "--no-dev");
    }

    [Fact]
    public void FilterComposerArgs_LeavesComposerVersionFlag()
    {
        ComposerAction.FilterComposerArgs(["--version"])
            .Should().Equal("--version");
    }

    [Theory]
    [InlineData(new[] { "install" }, true)]
    [InlineData(new[] { "update", "--with-dependencies" }, true)]
    [InlineData(new[] { "require", "foo/bar" }, true)]
    [InlineData(new[] { "--quiet=true", "install" }, true)]
    [InlineData(new[] { "--version" }, false)]
    [InlineData(new[] { "validate" }, false)]
    [InlineData(new string[0], false)]
    public void ShouldOfferTyhpdefHook_OnlyForInstallUpdateRequire(string[] args, bool expected)
    {
        ComposerAction.ShouldOfferTyhpdefHook(args).Should().Be(expected);
    }

    [Fact]
    public void IsSyncCommand_DetectsTyhpSyncSubcommand()
    {
        ComposerAction.IsSyncCommand(["sync"]).Should().BeTrue();
        ComposerAction.IsSyncCommand(["--no-tyhpdef", "sync"]).Should().BeTrue();
        ComposerAction.IsSyncCommand(["update"]).Should().BeFalse();
    }

    [Fact]
    public void FilterComposerArgs_KeepsSubcommandAfterBareNoTyhpdef()
    {
        // `--no-tyhpdef` takes no value, so the subcommand that follows it is Composer's.
        ComposerAction.FilterComposerArgs(["--no-tyhpdef", "require", "foo/bar"])
            .Should().Equal("require", "foo/bar");
    }

    [Fact]
    public void FilterComposerArgs_StripsExplicitBooleanLiteralAfterTyhpFlag()
    {
        ComposerAction.FilterComposerArgs(["--no-tyhpdef", "false", "install"])
            .Should().Equal("install");
    }

    [Fact]
    public void ReadInitialActionFromArgs_RemainingArgsPreserveRawComposerFlags()
    {
        string[] raw = ["composer", "install", "--no-tyhpdef", "--dry-run", "--prefer-dist"];
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(raw);

        ActionConfigProvider.ReadInitialActionFromArgs(expanded, raw).Should().BeTrue();

        // Composer's own flags must reach it exactly as typed: Symfony Console rejects
        // `--dry-run=true` on a value-less option.
        ActionConfigProvider.RemainingArgs.Should().Equal(
            "install",
            "--no-tyhpdef",
            "--dry-run",
            "--prefer-dist");
        ComposerAction.FilterComposerArgs(ActionConfigProvider.RemainingArgs)
            .Should().Equal("install", "--dry-run", "--prefer-dist");
        ActionConfigProvider.ExplicitPaths.Should().Equal("install");
    }

    [Fact]
    public void ReadInitialActionFromArgs_FallsBackToRewrittenArgsWhenVerbWasRewritten()
    {
        // `tyhp composer --help` becomes `help --subject=composer`, so the raw tokens belong to a
        // different action and must not be exposed as the proxied argv.
        string[] raw = ["composer", "--help"];
        var rewritten = ActionConfigProvider.RewriteHelpAlias(
            ActionConfigProvider.ExpandBareBooleanFlags(raw));

        ActionConfigProvider.ReadInitialActionFromArgs(rewritten, raw).Should().BeTrue();

        ActionConfigProvider.RemainingArgs.Should().Equal("--subject=composer");
    }

    [Fact]
    public void Install_PatchesAllowPluginsBeforeComposerRuns()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "tyhp.json"), """{ "include": ["src/**/*.tyhp"] }""");
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "require": {
                    "tyhp/core": "@dev"
                }
            }
            """);

        var sawAllowPlugins = false;
        Environment.ExitCode = (int)ExitCode.Success;
        var action = new ComposerAction(ProjectFor(root))
        {
            Args = ["install"],
            FindPhp = () => "/usr/bin/php",
            ResolveComposer = _ => "composer",
            RunComposerProcess = (_, _, _) =>
            {
                var composer = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
                sawAllowPlugins = composer.GetProperty("config")
                    .GetProperty("allow-plugins")
                    .GetProperty("tyhp/core")
                    .GetBoolean();
                return 0;
            },
        };

        action.Start(CancellationToken.None);

        Environment.ExitCode.Should().Be((int)ExitCode.Success);
        sawAllowPlugins.Should().BeTrue();
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement
            .GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Require_PatchesAllowPluginsBeforeComposerRunsEvenWithoutTyhpCore()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "tyhp.json"), """{ "include": ["src/**/*.tyhp"] }""");
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "name": "acme/existing"
            }
            """);

        var sawAllowPlugins = false;
        Environment.ExitCode = (int)ExitCode.Success;
        var action = new ComposerAction(ProjectFor(root))
        {
            Args = ["require", "tyhp/core"],
            FindPhp = () => "/usr/bin/php",
            ResolveComposer = _ => "composer",
            RunComposerProcess = (_, _, _) =>
            {
                var composer = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
                sawAllowPlugins = composer.GetProperty("config")
                    .GetProperty("allow-plugins")
                    .GetProperty("tyhp/core")
                    .GetBoolean();
                return 0;
            },
        };

        action.Start(CancellationToken.None);

        sawAllowPlugins.Should().BeTrue();
    }

    [Fact]
    public void Sync_CompilerRoot_StripsSelfRequireWithoutComposerUpdateOfCompiler()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "tyhp.json"), """{ "include": ["src/**/*.tyhp"] }""");
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "name": "tyhp/compiler",
                "require": {
                    "tyhp/core": "@dev"
                },
                "require-dev": {
                    "tyhp/compiler": "@dev",
                    "acme/tester": "^11.0"
                }
            }
            """);

        IReadOnlyList<string>? forwarded = null;
        Environment.ExitCode = (int)ExitCode.Success;
        var action = new ComposerAction(ProjectFor(root))
        {
            Args = ["sync"],
            FindPhp = () => "/usr/bin/php",
            ResolveComposer = _ => "composer",
            RunComposerProcess = (_, args, _) =>
            {
                forwarded = args;
                return 0;
            },
        };

        action.Start(CancellationToken.None);

        var composer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
        composer.GetProperty("require-dev").TryGetProperty("tyhp/compiler", out _).Should().BeFalse();
        composer.GetProperty("require-dev").GetProperty("acme/tester").GetString().Should().Be("^11.0");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        if (forwarded is not null)
        {
            forwarded.Should().NotContain("tyhp/compiler");
        }
    }

    [Fact]
    public void Install_DoesNotClobberExistingAllowPluginsFalse()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "tyhp.json"), """{ "include": ["src/**/*.tyhp"] }""");
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "config": {
                    "allow-plugins": {
                        "tyhp/core": false
                    }
                }
            }
            """);

        var sawFalse = false;
        Environment.ExitCode = (int)ExitCode.Success;
        var action = new ComposerAction(ProjectFor(root))
        {
            Args = ["update"],
            FindPhp = () => "/usr/bin/php",
            ResolveComposer = _ => "composer",
            RunComposerProcess = (_, _, _) =>
            {
                sawFalse = !JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json")))
                    .RootElement
                    .GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
                    .GetBoolean();
                return 0;
            },
        };

        action.Start(CancellationToken.None);

        sawFalse.Should().BeTrue();
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement
            .GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void TryEnsureAllowPlugins_IsNoOpWhenFileIsMissing()
    {
        var missing = Path.Combine(this.CreateTempDirectory(), "composer.json");
        InitComposerManifest.TryEnsureAllowPlugins(missing, out var error).Should().BeTrue();
        error.Should().BeNull();
        File.Exists(missing).Should().BeFalse();
    }

    [Fact]
    public void BareBooleanFlags_IncludesNoTyhpdef()
    {
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--no-tyhpdef");
    }

    [Fact]
    public void BareBooleanFlags_IncludesGenerateTyhpdefFlags()
    {
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--no-docs");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--include-internal");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--include-deprecated");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--no-deprecated");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--overwrite");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--no-php");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--verify");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--include-dev");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--require-stubs");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--no-php-runtime-update");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--refresh-snapshots");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--force");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--global");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--local");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--vendor");
    }

    private static Project ProjectFor(string root)
    {
        return new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = Path.Combine(root, "tyhp.json"),
                ["quiet"] = "true",
                ["no-tyhpdef"] = "true",
            })
            .Build());
    }

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyhp-composer-action", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        this._tempDirectories.Add(dir);
        return dir;
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
            }
        }
    }
}
