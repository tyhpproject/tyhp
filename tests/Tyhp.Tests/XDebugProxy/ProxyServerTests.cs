using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.XDebugProxy.Config;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.Network;
using Tyhp.XDebugProxy.SourceMap;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class ProxyServerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private const int TyhpDbgpLine = 42;
    private const int PhpDbgpLine = 67;

    [Fact]
    public void Config_Defaults_MatchStory()
    {
        var config = new XDebugProxyConfig();

        config.IdeListenPort.Should().Be(9003);
        config.XDebugListenPort.Should().Be(9004);
        config.IdeListenAddress.Should().Be("127.0.0.1");
        config.XDebugListenAddress.Should().Be("127.0.0.1");
        config.MaxSessions.Should().Be(10);
        config.LogLevel.Should().Be("info");
        config.AutoReloadSourceMaps.Should().BeTrue();
        config.IdeKey.Should().BeNull();
        config.PairingTimeout.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_BindsEphemeralPorts_AndAcceptsTcpOnBoth()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();

        harness.Server.BoundIdePort.Should().BeGreaterThan(0);
        harness.Server.BoundXDebugPort.Should().BeGreaterThan(0);
        harness.Server.BoundIdePort.Should().NotBe(harness.Server.BoundXDebugPort);

        using var ide = new TcpClient();
        using var xdebug = new TcpClient();
        await ide.ConnectAsync(IPAddress.Loopback, harness.Server.BoundIdePort);
        await xdebug.ConnectAsync(IPAddress.Loopback, harness.Server.BoundXDebugPort);

        ide.Connected.Should().BeTrue();
        xdebug.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task Pair_IdeThenXdebug_CreatesDebugSession_AndForwardsTranslatedInit()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        await using PairedPeers peers = await harness.PairAsync();

        peers.Session.Should().NotBeNull();
        harness.Server.ActiveSessionCount.Should().Be(1);

        DbgpResponse init = await ReadResponseAsync(peers.Ide);
        init.IsInit.Should().BeTrue();
        harness.Mapper.ToFileSystemPath(init.GetAttribute("fileuri")!)
            .Should().Be(harness.Mapper.Normalize(harness.TyhpFile));
        init.GetAttribute("fileuri").Should().Contain(".tyhp");
        init.GetAttribute("idekey").Should().Be("TYHP");
    }

    [Fact]
    public async Task Pair_XdebugThenIde_StillForwardsInit()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        await using PairedPeers peers = await harness.PairAsync(ideFirst: false);

        DbgpResponse init = await ReadResponseAsync(peers.Ide);
        init.IsInit.Should().BeTrue();
        init.GetAttribute("fileuri").Should().Contain(".tyhp");
    }

    [Fact]
    public async Task BidirectionalRelay_CommandReachesXdebug_ResponseReachesIde()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);

        await WriteCommandAsync(peers.Ide, DbgpMessageParser.ParseCommand("run -i 2"));
        DbgpCommand received = await ReadCommandAsync(peers.Xdebug);
        received.CommandName.Should().Be("run");
        received.TransactionId.Should().Be("2");

        DbgpResponse response = ParseResponse(
            """<response xmlns="urn:debugger_protocol_v1" command="run" transaction_id="2" status="break" reason="ok"/>""");
        await WriteResponseAsync(peers.Xdebug, response);

        DbgpResponse toIde = await ReadResponseAsync(peers.Ide);
        toIde.Command.Should().Be("run");
        toIde.TransactionId.Should().Be("2");
        toIde.Status.Should().Be("break");
    }

    [Fact]
    public async Task Relay_TranslatesBreakpointSet_TyhpPathAndLineToPhp()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);

        string tyhpUri = harness.Mapper.ToFileUri(harness.TyhpFile);
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {tyhpUri} -n {TyhpDbgpLine}");
        await WriteCommandAsync(peers.Ide, command);

        DbgpCommand atXdebug = await ReadCommandAsync(peers.Xdebug);
        harness.Mapper.ToFileSystemPath(atXdebug.Filename!)
            .Should().Be(harness.Mapper.Normalize(harness.PhpFile));
        atXdebug.Filename.Should().Contain(".php");
        atXdebug.LineNumber.Should().Be(PhpDbgpLine.ToString());
        atXdebug.TransactionId.Should().Be("1");
    }

    [Fact]
    public async Task DisconnectIde_ClosesXdebug_WithoutCrashing()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Server.SessionDisconnected += _ => disconnected.TrySetResult();

        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);

        peers.Ide.Close();
        await AssertPeerClosedAsync(peers.Xdebug);
        await disconnected.Task.WaitAsync(TestTimeout);
        harness.Server.ActiveSessionCount.Should().Be(0);
    }

    [Fact]
    public async Task SequentialSessions_PairFinishPairAgain()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Server.SessionDisconnected += _ => disconnected.TrySetResult();

        await using (PairedPeers first = await harness.PairAsync())
        {
            await DrainInitAsync(first.Ide);
            await WriteCommandAsync(first.Ide, DbgpMessageParser.ParseCommand("status -i 1"));
            DbgpCommand status = await ReadCommandAsync(first.Xdebug);
            status.CommandName.Should().Be("status");
        }

        await disconnected.Task.WaitAsync(TestTimeout);
        harness.Server.ActiveSessionCount.Should().Be(0);

        await using PairedPeers second = await harness.PairAsync();
        DbgpResponse init = await ReadResponseAsync(second.Ide);
        init.IsInit.Should().BeTrue();
        harness.Server.ActiveSessionCount.Should().Be(1);

        await WriteCommandAsync(second.Ide, DbgpMessageParser.ParseCommand("run -i 9"));
        DbgpCommand run = await ReadCommandAsync(second.Xdebug);
        run.CommandName.Should().Be("run");
        run.TransactionId.Should().Be("9");
    }

    [Fact]
    public async Task CancellationToken_StopsListenersAndSessions()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        int idePort = harness.Server.BoundIdePort;
        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);

        await harness.StopAsync();

        await AssertPeerClosedAsync(peers.Ide);
        await AssertPeerClosedAsync(peers.Xdebug);

        using var extra = new TcpClient();
        Func<Task> connect = () => extra.ConnectAsync(IPAddress.Loopback, idePort);
        await connect.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task MaxSessions_RejectsExtraConnections()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync(maxSessions: 1);
        var rejected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Server.ConnectionRejected += (_, reason) => rejected.TrySetResult(reason);

        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);
        harness.Server.ActiveSessionCount.Should().Be(1);

        await AssertConnectionRejectedAsync(harness.Server.BoundIdePort);
        string reason = await rejected.Task.WaitAsync(TestTimeout);
        reason.Should().Contain("MaxSessions");

        await AssertConnectionRejectedAsync(harness.Server.BoundXDebugPort);
        peers.Session.Should().NotBeNull();
        harness.Server.ActiveSessionCount.Should().Be(1);
    }

    [Fact]
    public async Task MaxSessions_RejectionEventHandler_DoesNotBlockOtherAcceptLoop()
    {
        // Regression test (Story 18 Phase 7 review): OnIdeAccepted/OnXdebugAcceptedAsync must
        // decide MaxSessions admission while holding `_gate`, but raise `ConnectionRejected`
        // (which may run arbitrary/slow subscriber code) only after releasing it. Before the
        // fix, a slow `ConnectionRejected` handler for a rejected IDE connection kept `_gate`
        // held, which stalled the XDebug accept loop's own MaxSessions check indefinitely.
        await using ProxyHarness harness = await ProxyHarness.StartAsync(maxSessions: 1);
        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);
        harness.Server.ActiveSessionCount.Should().Be(1);

        using var slowHandlerGate = new SemaphoreSlim(0, 1);
        var ideRejectedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Server.ConnectionRejected += (side, _) =>
        {
            if (side == "IDE")
            {
                ideRejectedStarted.TrySetResult();
                // Simulate a slow logger/subscriber. If `_gate` were still held here, this
                // would block the XDebug accept loop's MaxSessions check below.
                slowHandlerGate.Wait(TestTimeout);
            }
        };

        using var extraIde = new TcpClient();
        Task ideConnectTask = extraIde.ConnectAsync(IPAddress.Loopback, harness.Server.BoundIdePort);
        await ideRejectedStarted.Task.WaitAsync(TestTimeout);

        var xdebugRejected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Server.ConnectionRejected += (side, reason) =>
        {
            if (side == "XDebug")
            {
                xdebugRejected.TrySetResult(reason);
            }
        };

        using var extraXdebug = new TcpClient();
        Task xdebugConnectTask = extraXdebug.ConnectAsync(IPAddress.Loopback, harness.Server.BoundXDebugPort);

        // Must resolve promptly — well before the IDE handler's TestTimeout-long wait — proving
        // the XDebug accept loop was never blocked on `_gate`.
        string reason = await xdebugRejected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        reason.Should().Contain("MaxSessions");

        slowHandlerGate.Release();
        await ideConnectTask.WaitAsync(TestTimeout);
        await xdebugConnectTask.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task IdeKeyFilter_RejectsMismatch_ThenPairsMatch()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync(ideKey: "TYHP");
        var rejected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Server.ConnectionRejected += (_, reason) => rejected.TrySetResult(reason);

        using var waitingIde = new TcpClient();
        await waitingIde.ConnectAsync(IPAddress.Loopback, harness.Server.BoundIdePort);

        using var mismatch = new TcpClient();
        await mismatch.ConnectAsync(IPAddress.Loopback, harness.Server.BoundXDebugPort);
        await WriteResponseAsync(
            mismatch,
            CreateInit(harness.Mapper.ToFileUri(harness.PhpFile), "OTHER"));

        string reason = await rejected.Task.WaitAsync(TestTimeout);
        reason.Should().Contain("idekey");
        await AssertPeerClosedAsync(mismatch);

        await using PairedPeers peers = await harness.PairAsync(existingIde: waitingIde);
        DbgpResponse init = await ReadResponseAsync(peers.Ide);
        init.GetAttribute("idekey").Should().Be("TYHP");
        init.GetAttribute("fileuri").Should().Contain(".tyhp");
    }

    public static IEnumerable<object[]> AcceptLoopFailureCases()
    {
        yield return [new SocketException(), false, false];
        yield return [new SocketException(), true, true];
        yield return [new ObjectDisposedException(nameof(TcpListener)), false, true];
        yield return [new OperationCanceledException(), false, true];
    }

    [Theory]
    [MemberData(nameof(AcceptLoopFailureCases))]
    public void ShouldStopAcceptLoop_OnlyStopsForShutdownOrNonTransientFailures(
        Exception exception,
        bool cancellationRequested,
        bool expectedStop)
    {
        bool stop = ProxyServer.ShouldStopAcceptLoop(exception, cancellationRequested);

        stop.Should().Be(expectedStop);
    }

    [Fact]
    public async Task AcceptLoop_SurvivesTransientSocketException_AndStillAcceptsNextConnection()
    {
        // A single connection attempt that fails with a bare SocketException (e.g. the peer
        // reset the connection mid-handshake) must not permanently kill the accept loop —
        // regression test for the race described in ShouldStopAcceptLoop.
        await using ProxyHarness harness = await ProxyHarness.StartAsync();

        using var flaky = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        flaky.LingerState = new LingerOption(true, 0);
        await flaky.ConnectAsync(IPAddress.Loopback, harness.Server.BoundIdePort);
        flaky.Close();

        await using PairedPeers peers = await harness.PairAsync();
        peers.Session.Should().NotBeNull();
    }

    [Fact]
    public async Task MalformedXdebugPacket_IsSkipped_AndRelayContinues()
    {
        await using ProxyHarness harness = await ProxyHarness.StartAsync();
        await using PairedPeers peers = await harness.PairAsync();
        await DrainInitAsync(peers.Ide);

        byte[] xml = Encoding.UTF8.GetBytes("<not-xml");
        byte[] length = Encoding.UTF8.GetBytes(xml.Length.ToString());
        var framed = new byte[length.Length + 1 + xml.Length + 1];
        Buffer.BlockCopy(length, 0, framed, 0, length.Length);
        framed[length.Length] = DbgpConstants.NullByte;
        Buffer.BlockCopy(xml, 0, framed, length.Length + 1, xml.Length);
        framed[^1] = DbgpConstants.NullByte;
        await peers.Xdebug.GetStream().WriteAsync(framed);
        await peers.Xdebug.GetStream().FlushAsync();

        DbgpResponse valid = ParseResponse(
            """<response xmlns="urn:debugger_protocol_v1" command="status" transaction_id="4" status="break"/>""");
        await WriteResponseAsync(peers.Xdebug, valid);

        DbgpResponse toIde = await ReadResponseAsync(peers.Ide);
        toIde.Command.Should().Be("status");
        toIde.TransactionId.Should().Be("4");
    }

    private static async Task<DbgpCommand> ReadCommandAsync(TcpClient client)
    {
        return await TcpConnectionHandler.ReadCommandAsync(client.GetStream())
            .WaitAsync(TestTimeout);
    }

    private static async Task<DbgpResponse> ReadResponseAsync(TcpClient client)
    {
        return await TcpConnectionHandler.ReadResponseAsync(client.GetStream())
            .WaitAsync(TestTimeout);
    }

    private static Task WriteCommandAsync(TcpClient client, DbgpCommand command)
    {
        return TcpConnectionHandler.WriteCommandAsync(client.GetStream(), command)
            .WaitAsync(TestTimeout);
    }

    private static Task WriteResponseAsync(TcpClient client, DbgpResponse response)
    {
        return TcpConnectionHandler.WriteResponseAsync(client.GetStream(), response)
            .WaitAsync(TestTimeout);
    }

    private static async Task DrainInitAsync(TcpClient ide)
    {
        DbgpResponse init = await ReadResponseAsync(ide);
        init.IsInit.Should().BeTrue();
    }

    private static DbgpResponse CreateInit(string fileuri, string idekey)
    {
        return ParseResponse(
            $"""
            <init xmlns="urn:debugger_protocol_v1" fileuri="{fileuri}" language="PHP" protocol_version="1.0" appid="1" idekey="{idekey}">
              <engine version="3.3.0"><![CDATA[Xdebug]]></engine>
            </init>
            """);
    }

    private static DbgpResponse ParseResponse(string xml)
    {
        return new DbgpResponse(XElement.Parse(xml));
    }

    private static async Task AssertPeerClosedAsync(TcpClient client)
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        var buffer = new byte[1];
        try
        {
            int read = await client.GetStream().ReadAsync(buffer, cts.Token);
            read.Should().Be(0);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException("Peer did not close within the test timeout.");
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    private static async Task AssertConnectionRejectedAsync(int port)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(TestTimeout);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        }
        catch (SocketException)
        {
            return;
        }

        await AssertPeerClosedAsync(client);
    }

    private sealed class PairedPeers : IAsyncDisposable
    {
        public PairedPeers(TcpClient ide, TcpClient xdebug, DebugSession session, bool ownsIde)
        {
            this.Ide = ide;
            this.Xdebug = xdebug;
            this.Session = session;
            this._ownsIde = ownsIde;
        }

        public TcpClient Ide { get; }

        public TcpClient Xdebug { get; }

        public DebugSession Session { get; }

        private readonly bool _ownsIde;

        public async ValueTask DisposeAsync()
        {
            if (this._ownsIde)
            {
                this.Ide.Dispose();
            }

            this.Xdebug.Dispose();
            await Task.CompletedTask;
        }
    }

    private sealed class ProxyHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly List<string> _tempDirectories = [];
        private Task? _runTask;

        private ProxyHarness()
        {
        }

        public required XDebugProxyConfig Config { get; init; }

        public required ProxyServer Server { get; init; }

        public required SourceMapStore Store { get; init; }

        public required PathMapper Mapper { get; init; }

        public required string TyhpFile { get; init; }

        public required string PhpFile { get; init; }

        public static async Task<ProxyHarness> StartAsync(int maxSessions = 10, string? ideKey = null)
        {
            string root = CreateTempDirectory();
            string src = Path.Combine(root, "src");
            string build = Path.Combine(root, "build");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(build);
            string phpFile = Path.Combine(build, "App.php");
            string tyhpFile = Path.Combine(src, "App.tyhp");
            WriteMap(
                phpFile + ".map",
                file: "App.php",
                source: "src/App.tyhp",
                mappings: MappingAt(generatedLine0: 66, originalLine0: 41));

            var store = new SourceMapStore(build) { AutoReload = false };
            store.LoadAll();

            var config = new XDebugProxyConfig
            {
                IdeListenPort = 0,
                XDebugListenPort = 0,
                IdeListenAddress = "127.0.0.1",
                XDebugListenAddress = "127.0.0.1",
                SourceMapDirectory = build,
                TyhpSourceRoot = root,
                PhpOutputRoot = build,
                MaxSessions = maxSessions,
                IdeKey = ideKey,
                AutoReloadSourceMaps = false,
            };

            var server = new ProxyServer(config, store);
            var harness = new ProxyHarness
            {
                Config = config,
                Server = server,
                Store = store,
                Mapper = new PathMapper(root, build),
                TyhpFile = tyhpFile,
                PhpFile = phpFile,
            };
            harness._tempDirectories.Add(root);
            harness._runTask = server.StartAsync(harness._cts.Token);
            await server.WhenListening.WaitAsync(TestTimeout);
            return harness;
        }

        public async Task<PairedPeers> PairAsync(
            bool ideFirst = true,
            TcpClient? existingIde = null,
            string idekey = "TYHP")
        {
            var paired = new TaskCompletionSource<DebugSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPaired(DebugSession session) => paired.TrySetResult(session);
            this.Server.SessionPaired += OnPaired;

            TcpClient? ide = existingIde;
            TcpClient? xdebug = null;
            bool ownsIde = existingIde is null;
            try
            {
                string fileuri = this.Mapper.ToFileUri(this.PhpFile);
                if (ideFirst)
                {
                    ide ??= await ConnectAsync(this.Server.BoundIdePort);
                    xdebug = await ConnectAsync(this.Server.BoundXDebugPort);
                    await WriteResponseAsync(xdebug, CreateInit(fileuri, idekey));
                }
                else
                {
                    xdebug = await ConnectAsync(this.Server.BoundXDebugPort);
                    await WriteResponseAsync(xdebug, CreateInit(fileuri, idekey));
                    ide ??= await ConnectAsync(this.Server.BoundIdePort);
                }

                DebugSession session = await paired.Task.WaitAsync(TestTimeout);
                return new PairedPeers(ide, xdebug, session, ownsIde);
            }
            catch
            {
                if (ownsIde)
                {
                    ide?.Dispose();
                }

                xdebug?.Dispose();
                throw;
            }
            finally
            {
                this.Server.SessionPaired -= OnPaired;
            }
        }

        public async Task StopAsync()
        {
            this._cts.Cancel();
            await this.Server.StopAsync().WaitAsync(TestTimeout);
            if (this._runTask is not null)
            {
                try
                {
                    await this._runTask.WaitAsync(TestTimeout);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.StopAsync();
            }
            catch
            {
            }

            await this.Server.DisposeAsync();
            this.Store.Dispose();
            this._cts.Dispose();

            foreach (string directory in this._tempDirectories)
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

        private static async Task<TcpClient> ConnectAsync(int port)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TestTimeout);
                client.NoDelay = true;
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static string CreateTempDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        private static void WriteMap(string mapPath, string file, string source, string mappings)
        {
            string json =
                $$"""
                {
                  "version": 3,
                  "file": "{{file}}",
                  "sourceRoot": "",
                  "sources": ["{{source}}"],
                  "names": [],
                  "mappings": "{{mappings}}"
                }
                """;
            File.WriteAllText(mapPath, json);
        }

        private static string MappingAt(int generatedLine0, int originalLine0)
        {
            return new string(';', generatedLine0) + VlqEncoder.Encode([0, 0, originalLine0, 0]);
        }
    }
}
