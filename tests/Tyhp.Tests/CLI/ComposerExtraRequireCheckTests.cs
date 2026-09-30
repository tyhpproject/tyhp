using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class ComposerExtraRequireCheckTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void ConstraintCompatibility_MatchesPluginHeuristic()
    {
        ComposerConstraintCompatibility.AreCompatible("@dev", "@dev").Should().BeTrue();
        ComposerConstraintCompatibility.AreCompatible("^1.0", "*").Should().BeTrue();
        ComposerConstraintCompatibility.AreCompatible("^1.0", "^1.2").Should().BeTrue();
        ComposerConstraintCompatibility.AreCompatible("^1.0", "^2.0").Should().BeFalse();
        ComposerConstraintCompatibility.AreCompatible("@dev", "^2.0").Should().BeTrue();
    }

    [Fact]
    public void PlatformPackages_MatchPluginRegex()
    {
        ComposerPlatformPackages.IsPlatform("php").Should().BeTrue();
        ComposerPlatformPackages.IsPlatform("ext-widget").Should().BeTrue();
        ComposerPlatformPackages.IsPlatform("composer-plugin-api").Should().BeTrue();
        ComposerPlatformPackages.IsPlatform("composer-runtime-api").Should().BeTrue();
        ComposerPlatformPackages.IsPlatform("php-64bit").Should().BeTrue();
        ComposerPlatformPackages.IsPlatform("acme/widget").Should().BeFalse();
        ComposerPlatformPackages.IsPlatform("tyhpdef/acme-stubs").Should().BeFalse();
        ComposerPlatformPackages.IsPlatform("php-foo").Should().BeFalse();
    }

    [Fact]
    public void IsSelfRequire_MatchesRootNameAndIgnoresUnnamed()
    {
        ComposerExtraRequireGraph.IsSelfRequire("tyhp/compiler", "").Should().BeFalse();
        ComposerExtraRequireGraph.IsSelfRequire("tyhp/compiler", "__root__").Should().BeFalse();
        ComposerExtraRequireGraph.IsSelfRequire("tyhp/compiler", "tyhp/compiler").Should().BeTrue();
        ComposerExtraRequireGraph.IsSelfRequire("tyhp/compiler", "TyHp/Compiler").Should().BeTrue();
        ComposerExtraRequireGraph.IsSelfRequire("acme/widget", "tyhp/compiler").Should().BeFalse();
    }

    [Fact]
    public void Analyze_NoComposerJson_IsNotStale()
    {
        var root = this.CreateTempDirectory();
        var report = ComposerExtraRequireGraph.Analyze(root);
        report.NoComposerJson.Should().BeTrue();
        report.IsStale.Should().BeFalse();
    }

    [Fact]
    public void InstalledExtraNotInRootRequireDev_Is7701()
    {
        var root = this.WriteLibraryExtraFixture();
        var report = ComposerExtraRequireGraph.Analyze(root);

        report.Disjoint.Should().BeEmpty();
        report.MissingExtras.Should().Contain("tyhpdef/acme-stubs");
        report.CompilerGap.Should().BeFalse();

        var diagnostics = new DiagnosticBag();
        var ok = new ComposerExtraRequireCheck().ApplyReport(
            ProjectFor(root),
            diagnostics,
            report,
            fix: false,
            dryRun: false);

        ok.Should().BeFalse();
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.ComposerStaleExtraRequire);
        diagnostics.Errors.Should().Contain(d => d.Message.Contains("tyhpdef/acme-stubs", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultCheck_DoesNotInvokeComposer()
    {
        var root = this.WriteLibraryExtraFixture();
        var invoked = false;
        var diagnostics = new DiagnosticBag();
        new ComposerExtraRequireCheck
        {
            RunComposerProcess = (_, _, _) =>
            {
                invoked = true;
                return 0;
            },
        }.TryApply(ProjectFor(root), diagnostics, fix: false, dryRun: false);

        invoked.Should().BeFalse();
        diagnostics.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Fix_WritesMissingExtraOntoRequireDev()
    {
        var root = this.WriteLibraryExtraFixture();
        IReadOnlyList<string>? composerArgs = null;
        var diagnostics = new DiagnosticBag();
        new ComposerExtraRequireCheck
        {
            RunComposerProcess = (_, args, _) =>
            {
                composerArgs = args;
                InstallPackage(root, "tyhpdef/acme-stubs", extraRequire: null);
                return 0;
            },
        }.TryApply(ProjectFor(root), diagnostics, fix: true, dryRun: false);

        var composer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
        composer.GetProperty("require-dev").GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
        composerArgs.Should().NotBeNull();
        composerArgs!.Should().Contain("update");
        composerArgs.Should().Contain("tyhpdef/acme-stubs");
        composerArgs.Should().NotContain(a => a.Contains("packagist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryWriteRequireDev_WritesAllowPluginsForTyhpCore()
    {
        var root = this.CreateTempDirectory();
        var composerPath = Path.Combine(root, "composer.json");
        File.WriteAllText(composerPath, """
            {
                "require-dev": {
                    "acme/tester": "^11.0"
                }
            }
            """);

        ComposerExtraRequireCheck.TryWriteRequireDev(
            composerPath,
            new Dictionary<string, string> { ["acme/tester"] = "^11.0" },
            out var error).Should().BeTrue();
        error.Should().BeNull();

        var composer = JsonDocument.Parse(File.ReadAllText(composerPath)).RootElement;
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
        composer.GetProperty("require-dev").GetProperty("acme/tester").GetString().Should().Be("^11.0");
    }

    [Fact]
    public void TryWriteRequireDev_DoesNotClobberExistingAllowPluginsFalse()
    {
        var root = this.CreateTempDirectory();
        var composerPath = Path.Combine(root, "composer.json");
        File.WriteAllText(composerPath, """
            {
                "config": {
                    "allow-plugins": {
                        "tyhp/core": false
                    }
                }
            }
            """);

        ComposerExtraRequireCheck.TryWriteRequireDev(
            composerPath,
            new Dictionary<string, string>(),
            out var error).Should().BeTrue();
        error.Should().BeNull();

        JsonDocument.Parse(File.ReadAllText(composerPath)).RootElement
            .GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void MissingCompilerPin_Is7702WarningAndContinues()
    {
        var root = this.WriteCompilerGapFixture();
        var report = ComposerExtraRequireGraph.Analyze(root);
        report.CompilerGap.Should().BeTrue();
        report.HasBlockingErrors.Should().BeFalse();

        var diagnostics = new DiagnosticBag();
        var ok = new ComposerExtraRequireCheck().ApplyReport(
            ProjectFor(root),
            diagnostics,
            report,
            fix: false,
            dryRun: false);

        ok.Should().BeTrue();
        diagnostics.HasErrors.Should().BeFalse();
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.ComposerMissingCompilerPin);
        report.MergedRequireDev.Should().ContainKey("tyhp/compiler");
    }

    [Fact]
    public void Analyze_CompilerRoot_DoesNotPinSelfOrWarn()
    {
        var root = this.WriteCompilerRootFixture(includeSelfRequire: false);
        var report = ComposerExtraRequireGraph.Analyze(root);

        report.HasCore.Should().BeTrue();
        report.CompilerGap.Should().BeFalse();
        report.MergedRequireDev.Should().NotContainKey("tyhp/compiler");
        report.MergedRequireDev.Should().ContainKey("acme/tester");
        report.MergedRequireDev.Should().ContainKey("tyhpdef/acme-stubs");
        report.HasBlockingErrors.Should().BeTrue(
            "tyhpdef/acme-stubs from core extras is still missing from require-dev");

        var diagnostics = new DiagnosticBag();
        new ComposerExtraRequireCheck().ApplyReport(
            ProjectFor(root),
            diagnostics,
            report,
            fix: false,
            dryRun: false);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.ComposerMissingCompilerPin);
    }

    [Fact]
    public void Fix_CompilerRoot_StripsExistingSelfRequireAndKeepsOtherPins()
    {
        var root = this.WriteCompilerRootFixture(includeSelfRequire: true);
        IReadOnlyList<string>? composerArgs = null;
        var diagnostics = new DiagnosticBag();
        new ComposerExtraRequireCheck
        {
            RunComposerProcess = (_, args, _) =>
            {
                composerArgs = args;
                InstallPackage(root, "tyhpdef/acme-stubs", extraRequire: null);
                return 0;
            },
        }.TryApply(ProjectFor(root), diagnostics, fix: true, dryRun: false);

        var composer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
        var requireDev = composer.GetProperty("require-dev");
        requireDev.TryGetProperty("tyhp/compiler", out _).Should().BeFalse();
        requireDev.GetProperty("acme/tester").GetString().Should().Be("^11.0");
        requireDev.GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("@dev");
        composerArgs.Should().NotBeNull();
        composerArgs!.Should().Contain("tyhpdef/acme-stubs");
        composerArgs.Should().NotContain("tyhp/compiler");
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.ComposerMissingCompilerPin);
    }

    [Fact]
    public void Analyze_AppRoot_StillPinsCompiler()
    {
        var root = this.WriteCompilerGapFixture();
        var report = ComposerExtraRequireGraph.Analyze(root);
        report.MergedRequireDev.Should().ContainKey("tyhp/compiler");
        report.CompilerGap.Should().BeTrue();
    }

    [Fact]
    public void Build_MissingCompilerPin_WarnsAndContinuesWithoutStrict()
    {
        using var builder = this.CreateBuildProject(WriteCompilerGapFixture);
        var project = builder.BuildProject();
        var result = new BuildAction(project).Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.HasErrors.Should().BeFalse(
            "native CLI must warn-and-continue when the only gap is tyhp/compiler");
        result.Diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.ComposerMissingCompilerPin);
        result.GetExitCode(strictMode: false).Should().NotBe(Tyhp.Domain.Enums.ExitCode.CompileError);
    }

    [Fact]
    public void Build_MissingCompilerPin_StrictFails()
    {
        using var builder = this.CreateBuildProject(WriteCompilerGapFixture);
        builder.WithConfigValue("strict", "true");
        var project = builder.BuildProject();
        project.Strict.Should().BeTrue();
        var result = new BuildAction(project).Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.ComposerMissingCompilerPin);
        result.GetExitCode(project.Strict).Should().Be(Tyhp.Domain.Enums.ExitCode.CompileError);
    }

    [Fact]
    public void Build_StaleExtra_FailsCompile()
    {
        using var builder = this.CreateBuildProject(WriteLibraryExtraFixture);
        var result = new BuildAction(builder.BuildProject()).Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Diagnostics.HasErrors.Should().BeTrue();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.ComposerStaleExtraRequire);
    }

    /// <summary>
    /// <see cref="TestProjectBuilder"/> auto-seeds <c>vendor/composer/installed.json</c> from the
    /// root manifest so compiled-library unit fixtures that declare <c>extra.tyhp.require</c> without a
    /// real <c>composer install</c> are not flagged stale. A fixture that specifically wants the
    /// real "missing vendor" diagnostic must opt out by supplying its own (even empty)
    /// <c>vendor/composer/installed.json</c> — see <see cref="Build_MissingVendor_RealTyhpPackage_StillFailsWhenSeedIsOptedOut"/>.
    /// </summary>
    [Fact]
    public void Build_RequiredTyhpPackage_WithoutVendor_IsAutoSeededAsInstalled()
    {
        using var builder = new TestProjectBuilder()
            .WithTyhpFile("composer.json", """
                {
                    "require": { "tyhp/core": "@dev" }
                }
                """)
            .WithTyhpFile("src/app.tyhp", "<?tyhp\nfunction app_entry(): void {}\n");

        var result = builder.RunBuild();

        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.ComposerStaleExtraRequire);
    }

    /// <summary>
    /// Opt-out counterpart to <see cref="Build_RequiredTyhpPackage_WithoutVendor_IsAutoSeededAsInstalled"/>:
    /// supplying an explicit (empty) <c>vendor/composer/installed.json</c> fixture disables the
    /// <see cref="TestProjectBuilder"/> auto-seed, so the production check still diagnoses a
    /// <c>tyhp/</c> package that is required but genuinely not installed.
    /// </summary>
    [Fact]
    public void Build_MissingVendor_RealTyhpPackage_StillFailsWhenSeedIsOptedOut()
    {
        using var builder = new TestProjectBuilder()
            .WithTyhpFile("composer.json", """
                {
                    "require": { "tyhp/core": "@dev" }
                }
                """)
            .WithTyhpFile("vendor/composer/installed.json", """{ "packages": [] }""")
            .WithTyhpFile("src/app.tyhp", "<?tyhp\nfunction app_entry(): void {}\n");

        var result = builder.RunBuild();

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.ComposerStaleExtraRequire);
    }

    [Fact]
    public void DisjointExtraConstraint_Is7700()
    {
        var root = this.WriteDisjointFixture();
        var report = ComposerExtraRequireGraph.Analyze(root);
        report.Disjoint.Should().Contain(d =>
            d.Package == "tyhpdef/acme-stubs"
            && d.ExistingConstraint == "^1.0"
            && d.ExtraConstraint == "^2.0");

        var diagnostics = new DiagnosticBag();
        new ComposerExtraRequireCheck().ApplyReport(
            ProjectFor(root),
            diagnostics,
            report,
            fix: false,
            dryRun: false);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.ComposerDisjointExtraRequire);
    }

    [Fact]
    public void ComposerAction_IsSyncCommand()
    {
        ComposerAction.IsSyncCommand(["sync"]).Should().BeTrue();
        ComposerAction.IsSyncCommand(["--quiet", "sync"]).Should().BeTrue();
        ComposerAction.IsSyncCommand(["update"]).Should().BeFalse();
        ComposerAction.IsSyncCommand(["install"]).Should().BeFalse();
    }

    [Fact]
    public void ComposerSync_WritesRequireDevWithoutPackagist()
    {
        var root = this.WriteLibraryExtraFixture();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = Path.Combine(root, "tyhp.json"),
                ["quiet"] = "true",
            })
            .Build();
        File.WriteAllText(Path.Combine(root, "tyhp.json"), "{ \"include\": [\"src/**/*.tyhp\"] }");

        IReadOnlyList<string>? forwarded = null;
        var action = new ComposerAction(new Project(configuration))
        {
            Args = ["sync"],
            FindPhp = () => "/usr/bin/php",
            ResolveComposer = _ => "composer",
            RunComposerProcess = (_, args, _) =>
            {
                forwarded = args;
                InstallPackage(root, "tyhpdef/acme-stubs", extraRequire: null);
                return 0;
            },
        };

        action.Start(CancellationToken.None);

        var composer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
        composer.GetProperty("require-dev").GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
        composer.GetProperty("config").GetProperty("allow-plugins").GetProperty("tyhp/core")
            .GetBoolean().Should().BeTrue();
        forwarded.Should().NotBeNull();
        forwarded!.Should().Contain("update");
        forwarded.Should().NotContain(a => a.Contains("packagist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComposerSync_CompilerRoot_StripsSelfRequire()
    {
        var root = this.WriteCompilerRootFixture(includeSelfRequire: true);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = Path.Combine(root, "tyhp.json"),
                ["quiet"] = "true",
            })
            .Build();
        File.WriteAllText(Path.Combine(root, "tyhp.json"), "{ \"include\": [\"src/**/*.tyhp\"] }");

        IReadOnlyList<string>? forwarded = null;
        var action = new ComposerAction(new Project(configuration))
        {
            Args = ["sync"],
            FindPhp = () => "/usr/bin/php",
            ResolveComposer = _ => "composer",
            RunComposerProcess = (_, args, _) =>
            {
                forwarded = args;
                InstallPackage(root, "tyhpdef/acme-stubs", extraRequire: null);
                return 0;
            },
        };

        action.Start(CancellationToken.None);

        var composer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "composer.json"))).RootElement;
        var requireDev = composer.GetProperty("require-dev");
        requireDev.TryGetProperty("tyhp/compiler", out _).Should().BeFalse();
        requireDev.GetProperty("acme/tester").GetString().Should().Be("^11.0");
        requireDev.GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
        forwarded.Should().NotBeNull();
        forwarded!.Should().NotContain("tyhp/compiler");
    }

    [Fact]
    public void InvalidRootComposerJson_Is7703()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "composer.json"), "[1, 2]");
        var report = ComposerExtraRequireGraph.Analyze(root);
        report.ParseError.Should().NotBeNull();

        var diagnostics = new DiagnosticBag();
        new ComposerExtraRequireCheck().ApplyReport(
            ProjectFor(root),
            diagnostics,
            report,
            fix: false,
            dryRun: false);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.ComposerRootJsonWriteFailed);
    }

    private TestProjectBuilder CreateBuildProject(Func<string> writeFixture)
    {
        var fixtureRoot = writeFixture();
        var builder = new TestProjectBuilder();
        foreach (var file in Directory.GetFiles(fixtureRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fixtureRoot, file).Replace('\\', '/');
            builder.WithTyhpFile(relative, File.ReadAllText(file));
        }

        builder.WithTyhpFile("src/index.tyhp", "<?tyhp\nnamespace App;\nclass Demo {}\n");
        builder.WithDefaultTyhpJson();
        builder.WithConfigValue("quiet", "true");
        return builder;
    }

    private string WriteLibraryExtraFixture()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "require": {
                    "acme/lib": "@dev"
                }
            }
            """);
        InstallPackage(root, "acme/lib", extraRequire: """{ "tyhpdef/acme-stubs": "@dev" }""");
        return root;
    }

    private string WriteCompilerGapFixture()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "require": {
                    "tyhp/core": "@dev"
                }
            }
            """);
        InstallPackage(root, "tyhp/core", extraRequire: "{}");
        return root;
    }

    private string WriteCompilerRootFixture(bool includeSelfRequire)
    {
        var root = this.CreateTempDirectory();
        var selfRequire = includeSelfRequire
            ? """
                    "tyhp/compiler": "@dev",
            """
            : "";
        File.WriteAllText(Path.Combine(root, "composer.json"), $$"""
            {
                "name": "TyHp/Compiler",
                "require": {
                    "tyhp/core": "@dev"
                },
                "require-dev": {
                    {{selfRequire}}
                    "acme/tester": "^11.0"
                }
            }
            """);
        InstallPackage(root, "tyhp/core", extraRequire: """{ "tyhpdef/acme-stubs": "@dev" }""");
        return root;
    }

    private string WriteDisjointFixture()
    {
        var root = this.CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "composer.json"), """
            {
                "require": {
                    "acme/lib": "@dev"
                },
                "require-dev": {
                    "tyhpdef/acme-stubs": "^1.0"
                }
            }
            """);
        InstallPackage(root, "acme/lib", extraRequire: """{ "tyhpdef/acme-stubs": "^2.0" }""");
        InstallPackage(root, "tyhpdef/acme-stubs", extraRequire: null);
        return root;
    }

    private static void InstallPackage(string projectRoot, string packageName, string? extraRequire)
    {
        var slash = packageName.IndexOf('/');
        var vendor = packageName[..slash];
        var name = packageName[(slash + 1)..];
        var installPath = Path.Combine(projectRoot, "vendor", vendor, name);
        Directory.CreateDirectory(installPath);
        var extraJson = extraRequire is null
            ? ""
            : $$"""
                ,
                "extra": {
                    "tyhp": {
                        "require": {{extraRequire}}
                    }
                }
                """;
        File.WriteAllText(Path.Combine(installPath, "composer.json"), $$"""
            {
                "name": "{{packageName}}"{{extraJson}}
            }
            """);

        RewriteInstalledJson(projectRoot);
    }

    private static void RewriteInstalledJson(string projectRoot)
    {
        var vendor = Path.Combine(projectRoot, "vendor");
        var composerDir = Path.Combine(vendor, "composer");
        Directory.CreateDirectory(composerDir);

        var packages = new JsonArray();
        foreach (var composerFile in Directory.GetFiles(vendor, "composer.json", SearchOption.AllDirectories))
        {
            if (composerFile.Contains(
                    Path.DirectorySeparatorChar + "composer" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var json = JsonNode.Parse(File.ReadAllText(composerFile)) as JsonObject;
            var pkgName = json?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(pkgName))
            {
                continue;
            }

            var slash = pkgName.IndexOf('/');
            if (slash <= 0)
            {
                continue;
            }

            packages.Add(new JsonObject
            {
                ["name"] = pkgName,
                ["version"] = "dev-main",
                ["install-path"] = $"../{pkgName[..slash]}/{pkgName[(slash + 1)..]}",
            });
        }

        var payload = new JsonObject
        {
            ["packages"] = packages,
            ["packages-dev"] = new JsonArray(),
        };
        File.WriteAllText(
            Path.Combine(composerDir, "installed.json"),
            payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Project ProjectFor(string root)
    {
        var tyhpJson = Path.Combine(root, "tyhp.json");
        if (!File.Exists(tyhpJson))
        {
            File.WriteAllText(tyhpJson, """{ "include": ["src/**/*.tyhp"] }""");
        }

        return new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = tyhpJson,
                ["quiet"] = "true",
            })
            .Build());
    }

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyhp-extras-check", Guid.NewGuid().ToString("N"));
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
