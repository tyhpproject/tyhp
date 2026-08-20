using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nerdbank.Streams;
using Tyhp.Domain.Enums;
using Tyhp.LanguageServer;

namespace Tyhp.Tests.CLI;

/// <summary>
/// Regression coverage for the <c>language_server</c> case in <see cref="Tyhp.CLI.TyhpHostedService"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="TyhpHostedServiceXDebugProxyTests"/>: calling the blocking
/// <c>LanguageServerAction.Start()</c> inline from <c>IHostedService.StartAsync</c> would
/// deadlock startup and shutdown. Streams are overridden so the action does not steal the
/// test runner's stdin/stdout.
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "LanguageServer")]
public class TyhpHostedServiceLanguageServerTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task LanguageServer_StartThenStop_DoesNotDeadlockHostLifecycle()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var configPath = Path.Combine(root, "tyhp.json");
        await File.WriteAllTextAsync(configPath, "{}\n");

        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        await using var serverOwner = serverStream;
        await using var clientOwner = clientStream;
        LanguageServerAction.OverrideStreams = (serverStream, serverStream);

        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["*action"] = "language_server",
            ["quiet"] = "true",
        });

        try
        {
            Environment.ExitCode = 0;
            await host.StartAsync().WaitAsync(TestTimeout);
            Environment.ExitCode.Should().Be((int)ExitCode.Success);
            await host.StopAsync().WaitAsync(TestTimeout);
        }
        finally
        {
            LanguageServerAction.OverrideStreams = null;
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task LanguageServer_TcpFlag_StartAsyncReturnsAndHostStopsWithoutHanging()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var configPath = Path.Combine(root, "tyhp.json");
        await File.WriteAllTextAsync(configPath, "{}\n");

        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["*action"] = "language_server",
            ["quiet"] = "true",
            ["tcp"] = "2087",
        });

        try
        {
            Environment.ExitCode = 0;
            await host.StartAsync().WaitAsync(TestTimeout);
            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);

            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.IsCancellationRequested.Should().BeTrue();

            await host.StopAsync().WaitAsync(TestTimeout);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task LanguageServer_DoesNotWriteDefaultPidFileInProjectDirectory()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var configPath = Path.Combine(root, "tyhp.json");
        await File.WriteAllTextAsync(configPath, "{}\n");

        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        await using var serverOwner = serverStream;
        await using var clientOwner = clientStream;
        LanguageServerAction.OverrideStreams = (serverStream, serverStream);

        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["*action"] = "language_server",
            ["quiet"] = "true",
        });

        try
        {
            Environment.ExitCode = 0;
            await host.StartAsync().WaitAsync(TestTimeout);
            File.Exists(Path.Combine(root, "tyhp.pid")).Should().BeFalse();
            await host.StopAsync().WaitAsync(TestTimeout);
            File.Exists(Path.Combine(root, "tyhp.pid")).Should().BeFalse();
        }
        finally
        {
            LanguageServerAction.OverrideStreams = null;
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task LanguageServer_WritesConfiguredPidFileAndDeletesOnStop()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var configPath = Path.Combine(root, "tyhp.json");
        var pidPath = Path.Combine(root, "custom.pid");
        await File.WriteAllTextAsync(configPath, "{}\n");

        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        await using var serverOwner = serverStream;
        await using var clientOwner = clientStream;
        LanguageServerAction.OverrideStreams = (serverStream, serverStream);

        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["*action"] = "language_server",
            ["quiet"] = "true",
            ["pid-file"] = pidPath,
        });

        try
        {
            Environment.ExitCode = 0;
            await host.StartAsync().WaitAsync(TestTimeout);
            File.Exists(pidPath).Should().BeTrue();
            (await File.ReadAllTextAsync(pidPath)).Trim().Should().Be(Environment.ProcessId.ToString());
            await host.StopAsync().WaitAsync(TestTimeout);
            File.Exists(pidPath).Should().BeFalse();
        }
        finally
        {
            LanguageServerAction.OverrideStreams = null;
            Environment.ExitCode = previous;
        }
    }

    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHostedService<Tyhp.CLI.TyhpHostedService>();
                services.AddLocalization(options =>
                {
                    options.ResourcesPath = "Resources";
                });
            })
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            })
            .Build();
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-hosted-service-language-server", Guid.NewGuid().ToString("N"));
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
            }
        }

        GC.SuppressFinalize(this);
    }
}
