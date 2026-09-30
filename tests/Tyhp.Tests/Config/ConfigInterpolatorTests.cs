using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Config;

[Trait("Category", "Config")]
public class ConfigInterpolatorTests
{
    [Theory]
    [InlineData("8.2", "802")]
    [InlineData("8.6", "806")]
    [InlineData("8.0", "800")]
    [InlineData("8.10", "810")]
    [InlineData("8.4.1", "804")]
    public void ToPhpId_UsesMajorAndTwoDigitMinor(string phpVersion, string expected)
    {
        ConfigInterpolator.ToPhpId(phpVersion).Should().Be(expected);
    }

    [Fact]
    public void Project_ExpandsComposerNameAndVersion()
    {
        using var layout = Layout.Create(
            """
            {
                "output": {
                    "path": "build/{phpVersion.id}",
                    "publishPath": "./publish/{version}"
                }
            }
            """,
            """
            {
                "name": "acme/logger",
                "version": "3.0.0",
                "type": "library"
            }
            """);

        layout.Project.Output.Path.Should().Be("build/802");
        layout.Project.Output.PublishPath.Should().Be("./publish/3.0.0");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Project_ExpandsNamePartsAndComposerType()
    {
        using var layout = Layout.Create(
            """
            {
                "type": "application",
                "output": {
                    "publishPath": "{name.vendor}/{name.package}/{name.slug}/{composer.type}/{type}"
                }
            }
            """,
            """
            {
                "name": "acme/logger",
                "version": "3.0.0",
                "type": "library"
            }
            """);

        layout.Project.Output.PublishPath.Should().Be("acme/logger/acme-logger/library/application");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Project_ExpandsNameWithSlashAsTwoSegments()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "publishPath": "./vendor/{name}" }
            }
            """,
            """{ "name": "acme/logger", "version": "1.0.0" }""");

        layout.Project.Output.PublishPath.Should().Be("./vendor/acme/logger");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Project_PhpVersionId_DoesNotRequireComposerJson()
    {
        using var layout = Layout.Create(
            """
            {
                "output": {
                    "phpVersion": "8.5",
                    "path": "out/{phpVersion}/{phpVersion.major}/{phpVersion.minor}/{phpVersion.id}"
                }
            }
            """);

        layout.Project.Output.Path.Should().Be("out/8.5/8/5/805");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Project_ProfileDefaultsToDebug()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "path": "build/{profile}" }
            }
            """);

        layout.Project.Output.Path.Should().Be("build/debug");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Project_ProjectVersionAlias_MatchesVersion()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "publishPath": "{projectVersion}/{version.major}.{version.minor}.{version.patch}" }
            }
            """,
            """{ "name": "acme/pkg", "version": "3.4.1" }""");

        layout.Project.Output.PublishPath.Should().Be("3.4.1/3.4.1");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Theory]
    [InlineData("3.4.1", "3", "4", "1", "")]
    [InlineData("3.4.1-beta.1", "3", "4", "1", "-beta.1")]
    [InlineData("3.4.1+build.5", "3", "4", "1", "+build.5")]
    [InlineData("5.34.12.5.3.22", "5", "34", "12", ".5.3.22")]
    [InlineData("805.1.0-beta.1", "805", "1", "0", "-beta.1")]
    [InlineData("3.4", "3", "4", "0", "")]
    [InlineData("3.4-rc.1", "3", "4", "0", "-rc.1")]
    public void SplitComposerVersion_SeparatesNumericPartsAndSuffix(
        string version,
        string major,
        string minor,
        string patch,
        string suffix)
    {
        var parts = ConfigInterpolator.SplitComposerVersion(version);
        parts.Major.Should().Be(major);
        parts.Minor.Should().Be(minor);
        parts.Patch.Should().Be(patch);
        parts.Suffix.Should().Be(suffix);
    }

    [Fact]
    public void Project_VersionSuffix_IsEmptyWhenAbsent()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "path": "build/{version.major}.{version.minor}.{version.patch}{version.suffix}" }
            }
            """,
            """{ "name": "acme/pkg", "version": "3.4.1" }""");

        layout.Project.HasPendingConfigErrors.Should().BeFalse();
        layout.Project.Output.Path.Should().Be("build/3.4.1");
    }

    [Fact]
    public void Project_VersionSuffix_KeepsPrereleaseAndBuild()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "publishPath": "./publish/{version.major}.{version.minor}.{version.patch}{version.suffix}" }
            }
            """,
            """{ "name": "acme/pkg", "version": "3.4.1-beta.1+exp.sha" }""");

        layout.Project.HasPendingConfigErrors.Should().BeFalse();
        layout.Project.Output.PublishPath.Should().Be("./publish/3.4.1-beta.1+exp.sha");
    }

    [Fact]
    public void Project_DoubledBraces_BecomeLiteral()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "path": "build/{{version}}" }
            }
            """);

        layout.Project.Output.Path.Should().Be("build/{version}");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Project_UnknownVariable_IsConfigError()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "path": "build/{nope}" }
            }
            """);

        layout.Project.HasPendingConfigErrors.Should().BeTrue();
        var diagnostics = Transfer(layout.Project);
        diagnostics.Should().Contain(d => d.Code == MessageCode.ConfigInterpolationFailed);
        diagnostics[0].FormatParams.Should().Contain("nope");
    }

    [Fact]
    public void Project_MissingComposerJson_WhenVersionUsed_IsError()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "publishPath": "./publish/{version}" }
            }
            """);

        layout.Project.HasPendingConfigErrors.Should().BeTrue();
        Transfer(layout.Project).Should().Contain(d => d.Code == MessageCode.ConfigInterpolationFailed);
        layout.Project.Output.PublishPath.Should().Be("./publish/{version}");
    }

    [Fact]
    public void Project_MissingComposerVersion_IsError()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "publishPath": "./publish/{version}" }
            }
            """,
            """{ "name": "acme/pkg" }""");

        layout.Project.HasPendingConfigErrors.Should().BeTrue();
        Transfer(layout.Project)[0].FormatParams.Should().Contain(p => p.ToString()!.Contains("version"));
    }

    [Fact]
    public void Project_EnvVariable_Expands()
    {
        const string envName = "TYHP_INTERP_TEST_TAG";
        var previous = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, "v1.2.3");
        try
        {
            using var layout = Layout.Create(
                """
                {
                    "output": { "publishPath": "./publish/{env.TYHP_INTERP_TEST_TAG}" }
                }
                """);

            layout.Project.Output.PublishPath.Should().Be("./publish/v1.2.3");
            layout.Project.HasPendingConfigErrors.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void Project_MissingEnvVariable_IsError()
    {
        const string envName = "TYHP_INTERP_TEST_UNSET";
        var previous = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, null);
        try
        {
            using var layout = Layout.Create(
                $$"""
                {
                    "output": { "path": "build/{env.{{envName}}}" }
                }
                """);

            layout.Project.HasPendingConfigErrors.Should().BeTrue();
            Transfer(layout.Project).Should().Contain(d => d.Code == MessageCode.ConfigInterpolationFailed);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void Project_FileVariableInPath_IsError()
    {
        using var layout = Layout.Create(
            """
            {
                "output": { "path": "build/{fileName}" }
            }
            """);

        layout.Project.HasPendingConfigErrors.Should().BeTrue();
        Transfer(layout.Project)[0].FormatParams.Should().Contain("fileName");
    }

    [Fact]
    public void Project_LeavesFilePlaceholdersInPublishContentDst()
    {
        using var layout = Layout.Create(
            """
            {
                "output": {
                    "publishPath": "./publish/{version}",
                    "publishContent": [
                        { "src": "docs/**/*.md", "dst": "{recursiveDir}/{fileStem}.txt" }
                    ]
                }
            }
            """,
            """{ "name": "acme/pkg", "version": "2.1.0" }""");

        layout.Project.HasPendingConfigErrors.Should().BeFalse();
        layout.Project.Output.PublishPath.Should().Be("./publish/2.1.0");
        layout.Project.Output.PublishContent.Should().ContainSingle()
            .Which.Dst.Should().Be("{recursiveDir}/{fileStem}.txt");
    }

    [Fact]
    public void Project_Psr4Value_Interpolates()
    {
        using var layout = Layout.Create(
            """
            {
                "psr4": { "App\\": "src/{phpVersion.id}/" }
            }
            """);

        layout.Project.Build.Psr4.Should().ContainKey("App\\").WhoseValue.Should().Be("src/802/");
        layout.Project.HasPendingConfigErrors.Should().BeFalse();
    }

    [Fact]
    public void Build_InterpolationError_DoesNotCreateLiteralPlaceholderDirectory()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": {
                        "path": "build",
                        "publishPath": "./publish/{version}",
                        "publishClean": true
                    }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                class App {
                    public function ping(): int {
                        return 1;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().Contain(e => e.Code == MessageCode.ConfigInterpolationFailed);
        Directory.Exists(Path.Combine(project.ProjectDirectory, "publish", "{version}"))
            .Should().BeFalse();
    }

    [Fact]
    public void TryExpandFileDestination_AllowsEmptyExtensionAndFileStem()
    {
        var errors = new List<(MessageCode Code, object[] Args)>();
        Action<MessageCode, object[]> report = (code, args) => errors.Add((code, args));

        ConfigInterpolator.TryExpandFileDestination(
                "{fileStem}{extension}",
                "/proj/README",
                "README",
                "output.publishContent.dst",
                report,
                out var noExt)
            .Should().BeTrue();
        errors.Should().BeEmpty();
        noExt.Should().Be("README");

        ConfigInterpolator.TryExpandFileDestination(
                "{fileStem}{extension}",
                "/proj/.gitignore",
                ".gitignore",
                "output.publishContent.dst",
                report,
                out var dotfile)
            .Should().BeTrue();
        errors.Should().BeEmpty();
        dotfile.Should().Be(".gitignore");
    }

    [Fact]
    public void TryExpandFileDestination_AllowsEmptyRecursiveDir()
    {
        var errors = new List<(MessageCode Code, object[] Args)>();
        Action<MessageCode, object[]> report = (code, args) => errors.Add((code, args));

        ConfigInterpolator.TryExpandFileDestination(
                "{recursiveDir}/{fileName}",
                "/proj/README.md",
                "README.md",
                "output.publishContent.dst",
                report,
                out var expanded)
            .Should().BeTrue();
        errors.Should().BeEmpty();
        expanded.Should().Be("/README.md");
    }

    [Fact]
    public void Build_EscapedFileVariableInDst_ProducesLiteralBraceText()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": {
                        "path": "build",
                        "publishPath": "publish",
                        "publishContent": [
                            { "src": "docs/**/*.md", "dst": "{{fileName}}/{fileName}" }
                        ]
                    }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                class App {
                    public function ping(): int {
                        return 1;
                    }
                }
                """)
            .WithTyhpFile("docs/intro.md", "intro");

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "publish", "{fileName}", "intro.md"))
            .Should().Be("intro");
    }

    [Fact]
    public void Project_LeavesDoubledBracesInPublishContentDstUntilCopyTime()
    {
        using var layout = Layout.Create(
            """
            {
                "output": {
                    "publishContent": [
                        { "src": "docs/**/*.md", "dst": "{{version}}/{fileName}" }
                    ]
                }
            }
            """,
            """{ "name": "acme/pkg", "version": "2.1.0" }""");

        layout.Project.HasPendingConfigErrors.Should().BeFalse();
        layout.Project.Output.PublishContent.Should().ContainSingle()
            .Which.Dst.Should().Be("{{version}}/{fileName}");
    }

    [Fact]
    public void Build_EscapedComposerVariableInDst_ProducesLiteralBraceTextNotTheRealValue()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": {
                        "path": "build",
                        "publishPath": "publish",
                        "publishContent": [
                            { "src": "docs/**/*.md", "dst": "{{version}}/{fileName}" }
                        ]
                    }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                class App {
                    public function ping(): int {
                        return 1;
                    }
                }
                """)
            .WithTyhpFile("docs/intro.md", "intro")
            .WithTyhpFile("composer.json", """{ "name": "acme/pkg", "version": "2.1.0" }""");

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "publish", "{version}", "intro.md"))
            .Should().Be("intro");
        Directory.Exists(Path.Combine(project.ProjectDirectory, "publish", "2.1.0"))
            .Should().BeFalse();
    }

    private static IReadOnlyList<IDiagnostic> Transfer(Project project)
    {
        var bag = new DiagnosticBag();
        project.TransferPendingConfigWarningsTo(bag);
        return bag.Errors;
    }

    private sealed class Layout : IDisposable
    {
        private Layout(string dir, Project project)
        {
            this.Dir = dir;
            this.Project = project;
        }

        public string Dir { get; }

        public Project Project { get; }

        public static Layout Create(string tyhpJson, string? composerJson = null)
        {
            var dir = Path.Combine(Path.GetTempPath(), "tyhp-interp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var tyhpPath = Path.Combine(dir, "tyhp.json");
            File.WriteAllText(tyhpPath, tyhpJson);
            if (composerJson is not null)
            {
                File.WriteAllText(Path.Combine(dir, "composer.json"), composerJson);
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(dir)
                .AddJsonFile("tyhp.json")
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = tyhpPath,
                })
                .Build();

            return new Layout(dir, new Project(configuration));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Dir))
                {
                    Directory.Delete(this.Dir, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
