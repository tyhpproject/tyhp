using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tyhp.Domain.Enums;

namespace Tyhp.Tests.CLI;

/// <summary>
/// Regression coverage for the <c>xdebug_proxy</c> case in <see cref="Tyhp.CLI.TyhpHostedService"/>.
/// </summary>
/// <remarks>
/// <see cref="Tyhp.Tests.CLI.XDebugProxyActionTests"/> exercises <c>XDebugProxyAction</c> directly,
/// which bypasses the hosted-service orchestration entirely. These tests instead drive
/// <c>TyhpHostedService</c> through a real <see cref="IHost"/>, because the bug this guards against
/// (calling the blocking <c>XDebugProxyAction.Start()</c> inline from
/// <c>IHostedService.StartAsync</c>) only manifests at that layer: the .NET Generic Host does not
/// invoke <c>StopAsync</c> (which cancels the action's token) until every hosted service's
/// <c>StartAsync</c> has returned, so calling the blocking method inline deadlocks startup and
/// shutdown permanently.
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "XDebugProxy")]
public class TyhpHostedServiceXDebugProxyTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task XDebugProxy_StartThenStop_DoesNotDeadlockHostLifecycle()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var configPath = Path.Combine(root, "tyhp.json");
        await File.WriteAllTextAsync(configPath, "{}\n");

        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["*action"] = "xdebug_proxy",
            ["quiet"] = "true",
            ["ide-port"] = "0",
            ["xdebug-port"] = "0",
            ["xdebugProxy:autoReloadSourceMaps"] = "false",
        });

        try
        {
            Environment.ExitCode = 0;

            // StartAsync must return once the proxy has bound its ports, never once the proxy
            // stops. Prior to the fix this awaited XDebugProxyAction.Start() inline and hung here
            // forever, since nothing could cancel its token before StartAsync itself returned.
            await host.StartAsync().WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.Success);

            // StopAsync cancels the proxy's token and awaits its background task. Prior to the
            // fix, StartAsync never returned above, so this line — and real Ctrl+C shutdown —
            // was unreachable.
            await host.StopAsync().WaitAsync(TestTimeout);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task XDebugProxy_InvalidPort_StartAsyncReturnsAndHostStopsWithoutHanging()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var configPath = Path.Combine(root, "tyhp.json");
        await File.WriteAllTextAsync(configPath, "{}\n");

        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["*action"] = "xdebug_proxy",
            ["quiet"] = "true",
            ["ide-port"] = "-1",
        });

        try
        {
            Environment.ExitCode = 0;

            await host.StartAsync().WaitAsync(TestTimeout);

            // A bind/validation failure must not leave _isLongRunning true — that would idle the
            // host forever waiting for a Ctrl+C that can never arrive from an action that already
            // exited.
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-hosted-service-xdebug-proxy", Guid.NewGuid().ToString("N"));
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
