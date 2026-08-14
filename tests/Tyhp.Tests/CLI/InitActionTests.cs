using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Enums;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
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

        RunInit(target, new Dictionary<string, string?> { ["template"] = "laravel" });

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
            .Should().Equal("vendor/", "dist/", "tyhp.pid", ".tyhp-cache/");
    }

    [Fact]
    public void Init_DoesNotDuplicateExistingGitignoreEntries()
    {
        var target = this.CreateTempDirectory();
        var gitignorePath = Path.Combine(target, ".gitignore");
        File.WriteAllText(gitignorePath, "build/\ntyhp.pid\n.tyhp-cache/\n");

        RunInit(target);

        File.ReadAllLines(gitignorePath)
            .Should().Equal("build/", "tyhp.pid", ".tyhp-cache/");
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
        composer.GetProperty("require").GetProperty("tyhp/core").GetString().Should().Be("802.0.0");
        composer.GetProperty("require").GetProperty("tyhp/php").GetString().Should().Be("802.0.0");
    }

    private static void RunInit(string targetDirectory, Dictionary<string, string?>? options = null)
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
        using var action = new InitAction(project);
        action.Start(CancellationToken.None);
    }

    private static JsonDocument ReadConfig(string targetDirectory)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(targetDirectory, "tyhp.json")));

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
