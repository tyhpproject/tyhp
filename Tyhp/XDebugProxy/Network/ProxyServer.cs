using System.Collections.Concurrent;
using System.Net.Sockets;
using Tyhp.XDebugProxy.Config;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Network
{
    /// <summary>
    /// Dual TCP listener (IDE port + XDebug port). Accepts connections, pairs them into
    /// <see cref="DebugSession"/> instances, and tracks session tasks until shutdown.
    /// </summary>
    public sealed partial class ProxyServer : IAsyncDisposable, IDisposable
    {
        private readonly XDebugProxyConfig _config;
        private readonly SourceMapStore _sourceMapStore;
        private readonly Action<string>? _onLog;
        private readonly ProxyLogger _logger;
        private readonly object _gate = new();
        private readonly List<PendingIdeConnection> _pendingIdes = [];
        private readonly List<PendingXdebugConnection> _pendingXdebugs = [];
        private readonly List<TcpClient> _handshakeClients = [];
        private readonly ConcurrentDictionary<string, DebugSession> _sessions = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Task> _sessionTasks = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<int, Task> _backgroundTasks = new();
        private readonly TaskCompletionSource _listeningTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stoppedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _shutdownCts = new();
        private TcpListener? _ideListener;
        private TcpListener? _xdebugListener;
        private int _started;
        private int _stopping;
        private int _disposed;
        private int _sessionSerial;
        private int _backgroundSerial;
        private int _xdebugHandshakes;

        public ProxyServer(
            XDebugProxyConfig config,
            SourceMapStore sourceMapStore,
            Action<string>? onLog = null,
            ProxyLogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(sourceMapStore);

            this._config = config;
            this._sourceMapStore = sourceMapStore;
            this._onLog = onLog;
            // Do not attach onLog to ProxyLogger: debug XML dumps can contain "malformed"
            // and would false-trigger XDebugProxyAction's diagnostic string matching.
            this._logger = logger ?? new ProxyLogger(config.LogLevel);
            this._sourceMapStore.AutoReload = config.AutoReloadSourceMaps;
        }

        /// <summary>Actual IDE listen port after <see cref="StartAsync"/> begins listening (set when port 0 was requested).</summary>
        public int BoundIdePort { get; private set; }

        /// <summary>Actual XDebug listen port after <see cref="StartAsync"/> begins listening.</summary>
        public int BoundXDebugPort { get; private set; }

        /// <summary>Completes once both listeners are accepting connections.</summary>
        public Task WhenListening => this._listeningTcs.Task;

        /// <summary>Currently paired sessions (snapshot).</summary>
        public IReadOnlyList<DebugSession> ActiveSessions
        {
            get
            {
                return this._sessions.Values.ToArray();
            }
        }

        public int ActiveSessionCount => this._sessions.Count;

        /// <summary>Raised after an IDE and XDebug connection have been paired, before <c>&lt;init&gt;</c> is forwarded.</summary>
        public event Action<DebugSession>? SessionPaired;

        /// <summary>Raised after a session's relay has stopped and the session is removed from the active set.</summary>
        public event Action<DebugSession>? SessionDisconnected;

        /// <summary>Raised when a TCP client is accepted on either port (before pairing / init read).</summary>
        public event Action<string, string>? ConnectionAccepted;

        /// <summary>Raised when a TCP client is closed without becoming a session (max sessions, idekey filter, handshake failure).</summary>
        public event Action<string, string>? ConnectionRejected;

        /// <summary>
        /// Bind both listeners and run accept loops until <paramref name="cancellationToken"/>
        /// is cancelled or <see cref="StopAsync"/> is called.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(this._disposed != 0, this);
            if (this._config.MaxSessions < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(this._config),
                    this._config.MaxSessions,
                    "MaxSessions must be at least 1.");
            }

            if (Interlocked.CompareExchange(ref this._started, 1, 0) != 0)
            {
                throw new InvalidOperationException("ProxyServer has already been started.");
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                this._shutdownCts.Token);

            try
            {
                this.StartListeners();
                this._listeningTcs.TrySetResult();
                this.Log(
                    $"Listening on IDE {this._config.IdeListenAddress}:{this.BoundIdePort} "
                    + $"and XDebug {this._config.XDebugListenAddress}:{this.BoundXDebugPort}.");

                await Task.WhenAll(
                    this.AcceptIdeLoopAsync(linked.Token),
                    this.AcceptXdebugLoopAsync(linked.Token)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                this._listeningTcs.TrySetException(ex);
                throw;
            }
            finally
            {
                await this.StopCoreAsync().ConfigureAwait(false);
            }
        }

        public Task StopAsync()
        {
            try
            {
                this._shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            this.StopListeners();
            return this.StopCoreAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await this.StopAsync().ConfigureAwait(false);
            this.Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            try
            {
                this._shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            this.StopListeners();
            this.ClosePendingConnections();
            foreach (DebugSession session in this._sessions.Values)
            {
                session.CloseConnections();
            }

            this.DisposeManaged();
            GC.SuppressFinalize(this);
        }
    }
}
