using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Enums;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
[Trait("Category", "XDebugProxy")]
public class XDebugProxyActionTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task Start_EphemeralPorts_ListensThenStopsOnCancel()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root);
        using var action = new XDebugProxyAction(project);
        using var cts = new CancellationTokenSource();

        try
        {
            Environment.ExitCode = 0;
            var run = Task.Run(() => action.Start(cts.Token), CancellationToken.None);
            try
            {
                await action.WhenListening.WaitAsync(TestTimeout);
                action.BoundXDebugPort.Should().BeGreaterThan(0);
                action.BoundIdePort.Should().NotBe(action.BoundXDebugPort);

                using (var probe = new TcpClient())
                {
                    await probe.ConnectAsync(IPAddress.Loopback, action.BoundIdePort);
                    probe.Connected.Should().BeTrue();
                }
            }
            finally
            {
                cts.Cancel();
                await run.WaitAsync(TestTimeout);
            }

            Environment.ExitCode.Should().Be((int)ExitCode.Success);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_Cancel_PrintsShuttingDownBeforeStopped_NotSimultaneously()
    {
        // Regression (Story 18 Phase 7 review): the cancellation-registration callback used to
        // print "Shutting down... closing N session(s)." and "XDebug Proxy stopped." back to
        // back, immediately on Ctrl+C — before ProxyServer had actually finished closing
        // listeners/sessions. "Stopped" must only print once, strictly after the shutting-down
        // banner, once the underlying proxy task has actually completed.
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root, new Dictionary<string, string?> { ["quiet"] = "false" });
        using var action = new XDebugProxyAction(project);
        using var cts = new CancellationTokenSource();
        var recorder = new RecordingLocalizer();

        Message.SetLocalizer(recorder);
        try
        {
            Environment.ExitCode = 0;
            var run = action.RunAsync(cts.Token);
            await action.WhenListening.WaitAsync(TestTimeout);

            cts.Cancel();
            await run.WaitAsync(TestTimeout);

            var keys = recorder.Keys;
            int shuttingDownIndex = Array.IndexOf(keys, "CLI_XDebugProxyShuttingDown");
            int stoppedIndex = Array.IndexOf(keys, "CLI_XDebugProxyStopped");

            shuttingDownIndex.Should().BeGreaterThanOrEqualTo(0, "the shutting-down banner must print on cancellation");
            stoppedIndex.Should().BeGreaterThanOrEqualTo(0, "the stopped banner must print once the proxy actually stops");
            stoppedIndex.Should().BeGreaterThan(
                shuttingDownIndex,
                "\"stopped\" must print strictly after \"shutting down\", not simultaneously with it");
            keys.Count(k => k == "CLI_XDebugProxyStopped").Should().Be(1);
            keys.Count(k => k == "CLI_XDebugProxyShuttingDown").Should().Be(1);
        }
        finally
        {
            Message.ResetLocalizer();
            Environment.ExitCode = previous;
        }
    }

    private sealed class RecordingLocalizer : IStringLocalizer<TyhpHostedService>
    {
        private readonly ConcurrentQueue<string> _keys = new();

        public string[] Keys => this._keys.ToArray();

        public LocalizedString this[string name]
        {
            get
            {
                this._keys.Enqueue(name);
                return new LocalizedString(name, name);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                this._keys.Enqueue(name);
                return new LocalizedString(name, string.Format(name, arguments));
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => Enumerable.Empty<LocalizedString>();

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    [Fact]
    public async Task Start_NoSourceMaps_WarnsAndStillListens()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root);
        using var action = new XDebugProxyAction(project);
        using var cts = new CancellationTokenSource();

        try
        {
            Environment.ExitCode = 0;
            var run = action.RunAsync(cts.Token);
            try
            {
                await action.WhenListening.WaitAsync(TestTimeout);

                action.WarnedNoSourceMaps.Should().BeTrue();
                action.BoundIdePort.Should().BeGreaterThan(0);
            }
            finally
            {
                cts.Cancel();
                await run.WaitAsync(TestTimeout);
            }

            Environment.ExitCode.Should().Be((int)ExitCode.Success);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_InvalidPort_ErrorsAndDoesNotListen()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root, new Dictionary<string, string?>
        {
            ["ide-port"] = "-1",
        });
        using var action = new XDebugProxyAction(project);

        try
        {
            Environment.ExitCode = 0;
            await action.RunAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
            action.WhenListening.IsCompletedSuccessfully.Should().BeFalse();
            action.BoundIdePort.Should().Be(0);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_PortOutOfRange_ErrorsAndDoesNotListen()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root, new Dictionary<string, string?>
        {
            ["xdebug-port"] = "70000",
        });
        using var action = new XDebugProxyAction(project);

        try
        {
            Environment.ExitCode = 0;
            await action.RunAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
            action.WhenListening.IsCompletedSuccessfully.Should().BeFalse();
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_MissingExplicitSourceMapDir_ErrorsAndDoesNotListen()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var missing = Path.Combine(root, "no-such-maps");
        var project = CreateProject(root, new Dictionary<string, string?>
        {
            ["sourcemap-dir"] = missing,
        });
        using var action = new XDebugProxyAction(project);

        try
        {
            Environment.ExitCode = 0;
            await action.RunAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
            action.WhenListening.IsCompletedSuccessfully.Should().BeFalse();
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_PortInUse_ReportsProxyPortInUse()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        blocker.Start();
        int occupied = ((IPEndPoint)blocker.LocalEndpoint).Port;

        try
        {
            var project = CreateProject(root, new Dictionary<string, string?>
            {
                ["ide-port"] = occupied.ToString(),
                ["xdebug-port"] = "0",
            });
            using var action = new XDebugProxyAction(project);

            Environment.ExitCode = 0;
            await action.RunAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
            action.WhenListening.IsCompletedSuccessfully.Should().BeFalse();
        }
        finally
        {
            blocker.Stop();
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Start_InvalidListenAddress_ErrorsAndDoesNotListen()
    {
        var previous = Environment.ExitCode;
        var root = this.CreateTempDirectory();
        var project = CreateProject(root, new Dictionary<string, string?>
        {
            ["xdebugProxy:ideListenAddress"] = "not-an-address",
        });
        using var action = new XDebugProxyAction(project);

        try
        {
            Environment.ExitCode = 0;
            await action.RunAsync(CancellationToken.None).WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
            action.WhenListening.IsCompletedSuccessfully.Should().BeFalse();
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    private static Project CreateProject(string root, Dictionary<string, string?>? extra = null)
    {
        var configPath = Path.Combine(root, "tyhp.json");
        File.WriteAllText(configPath, "{}\n");

        var settings = new Dictionary<string, string?>
        {
            ["*project_file_path"] = configPath,
            ["quiet"] = "true",
            ["ide-port"] = "0",
            ["xdebug-port"] = "0",
            ["xdebugProxy:autoReloadSourceMaps"] = "false",
        };

        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build());
    }

    private string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy-cli", Guid.NewGuid().ToString("N"));
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
