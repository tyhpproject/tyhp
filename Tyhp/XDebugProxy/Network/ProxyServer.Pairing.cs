using System.Net.Sockets;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.XDebugProxy.Network
{
    public sealed partial class ProxyServer
    {
        private void OnIdeAccepted(TcpClient client)
        {
            ConfigureClient(client);
            this.RaiseAccepted("IDE", client);

            // Decide admission while holding `_gate` (atomic check-and-register against
            // MaxSessions), but raise the rejection log/event only after the lock is released —
            // logging (console I/O, subscriber callbacks) must never run while holding a lock
            // that the accept loops and TryPair also contend for.
            int maxSessions = this._config.MaxSessions;
            PendingIdeConnection? pending = null;
            bool rejected;
            lock (this._gate)
            {
                int ideSlots = this._sessions.Count + this._pendingIdes.Count;
                rejected = ideSlots >= maxSessions;
                if (!rejected)
                {
                    pending = new PendingIdeConnection(client);
                    this._pendingIdes.Add(pending);
                }
            }

            if (rejected)
            {
                this.RaiseRejected("IDE", client, $"MaxSessions ({maxSessions})");
                TryClose(client);
                return;
            }

            this.SchedulePairingTimeout(pending!, isIde: true);
            this.TryPair();
        }

        private async Task OnXdebugAcceptedAsync(TcpClient client, CancellationToken cancellationToken)
        {
            ConfigureClient(client);
            this.RaiseAccepted("XDebug", client);

            int maxSessions = this._config.MaxSessions;
            bool rejected;
            lock (this._gate)
            {
                int xdebugSlots = this._sessions.Count + this._pendingXdebugs.Count + this._xdebugHandshakes;
                rejected = xdebugSlots >= maxSessions;
                if (!rejected)
                {
                    this._xdebugHandshakes++;
                    this._handshakeClients.Add(client);
                }
            }

            if (rejected)
            {
                this.RaiseRejected("XDebug", client, $"MaxSessions ({maxSessions})");
                TryClose(client);
                return;
            }

            PendingXdebugConnection? pending = null;
            try
            {
                var handler = new TcpConnectionHandler(client);
                DbgpResponse init = await handler.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
                string? ideKey = init.GetAttribute("idekey");
                if (!this.IsIdeKeyAccepted(ideKey))
                {
                    this.RaiseRejected(
                        "XDebug",
                        client,
                        $"idekey '{ideKey}' does not match configured IdeKey '{this._config.IdeKey}'");
                    TryClose(client);
                    return;
                }

                pending = new PendingXdebugConnection(client, init, ideKey);
            }
            catch (Exception ex)
            {
                this.Log($"XDebug init packet could not be read: {ex.Message}");
                this.RaiseRejected("XDebug", client, "init handshake failed");
                TryClose(client);
            }
            finally
            {
                this.FinishXdebugHandshake(client, pending);
            }

            if (pending is null)
            {
                return;
            }

            this.SchedulePairingTimeout(pending, isIde: false);
            this.TryPair();
        }

        private bool IsIdeKeyAccepted(string? xdebugIdeKey)
        {
            if (string.IsNullOrEmpty(this._config.IdeKey))
            {
                return true;
            }

            return string.Equals(this._config.IdeKey, xdebugIdeKey, StringComparison.Ordinal);
        }

        private void FinishXdebugHandshake(TcpClient client, PendingXdebugConnection? enqueue)
        {
            lock (this._gate)
            {
                this._xdebugHandshakes = Math.Max(0, this._xdebugHandshakes - 1);
                this._handshakeClients.Remove(client);
                if (enqueue is not null)
                {
                    this._pendingXdebugs.Add(enqueue);
                }
            }
        }

        private void TryPair()
        {
            var newSessions = new List<DebugSession>();
            lock (this._gate)
            {
                while (this._pendingXdebugs.Count > 0 && this._pendingIdes.Count > 0)
                {
                    PendingXdebugConnection? matchedXdebug = null;
                    PendingIdeConnection? matchedIde = null;
                    foreach (PendingXdebugConnection xdebug in this._pendingXdebugs)
                    {
                        PendingIdeConnection? ide = FindMatchingIde(this._pendingIdes, xdebug.IdeKey);
                        if (ide is not null)
                        {
                            matchedXdebug = xdebug;
                            matchedIde = ide;
                            break;
                        }
                    }

                    if (matchedXdebug is null || matchedIde is null)
                    {
                        break;
                    }

                    this._pendingXdebugs.Remove(matchedXdebug);
                    this._pendingIdes.Remove(matchedIde);

                    // Register the session in `_sessions` while still holding `_gate`, the same
                    // lock `OnIdeAccepted`/`OnXdebugAcceptedAsync` use to compute
                    // `_sessions.Count + pending*.Count` against `MaxSessions`. If the pair were
                    // registered after releasing the lock, a connection racing this pairing
                    // could observe both counts transiently low (removed from pending, not yet
                    // counted as a session) and be admitted over the configured limit.
                    DebugSession session = this.CreatePairedSession(matchedIde, matchedXdebug);
                    this._sessions[session.SessionId] = session;
                    newSessions.Add(session);
                }
            }

            foreach (DebugSession session in newSessions)
            {
                this.LaunchSession(session);
            }
        }

        /// <summary>
        /// Prefer an IDE that advertised the same idekey. IDEs typically send nothing on
        /// connect, so <see cref="PendingIdeConnection.IdeKey"/> is null and pairing falls
        /// back to FIFO among those unlabelled clients. XDebug sessions that fail the
        /// configured <c>IdeKey</c> filter never reach this method.
        /// </summary>
        private static PendingIdeConnection? FindMatchingIde(
            List<PendingIdeConnection> ides,
            string? xdebugIdeKey)
        {
            if (!string.IsNullOrEmpty(xdebugIdeKey))
            {
                PendingIdeConnection? byKey = ides.FirstOrDefault(ide =>
                    string.Equals(ide.IdeKey, xdebugIdeKey, StringComparison.Ordinal));
                if (byKey is not null)
                {
                    return byKey;
                }

                return ides.FirstOrDefault(ide => string.IsNullOrEmpty(ide.IdeKey));
            }

            return ides.FirstOrDefault(ide => string.IsNullOrEmpty(ide.IdeKey))
                ?? (ides.Count > 0 ? ides[0] : null);
        }

        /// <summary>
        /// Build the paired <see cref="DebugSession"/>. Pure/local (no socket I/O) so it is
        /// safe to call while holding <see cref="_gate"/> from <see cref="TryPair"/>.
        /// </summary>
        private DebugSession CreatePairedSession(PendingIdeConnection ide, PendingXdebugConnection xdebug)
        {
            ide.CancelTimeout();
            xdebug.CancelTimeout();
            ide.DisposeTimeout();
            xdebug.DisposeTimeout();

            string sessionId = Interlocked.Increment(ref this._sessionSerial).ToString();
            PathMapper mapper = this.CreatePathMapper();
            var translator = new DbgpMessageTranslator(
                this._sourceMapStore,
                mapper,
                warning =>
                {
                    this.Log($"Session {sessionId} translation: {warning}");
                    this._logger.ForSession(sessionId).Warn("CLI_XDebugProxyTranslationFailed", warning);
                });

            return new DebugSession(
                sessionId,
                ide.Client,
                xdebug.Client,
                translator,
                xdebug.InitPacket,
                this._onLog,
                this._logger.ForSession(sessionId));
        }

        /// <summary>
        /// Raise pairing notifications and start the relay task. Called after
        /// <see cref="_gate"/> has been released — <see cref="RaiseSessionPaired"/> invokes
        /// external subscribers and starting the relay task begins real socket I/O, neither of
        /// which should happen while holding the lock.
        /// </summary>
        private void LaunchSession(DebugSession session)
        {
            this.Log(
                $"Session {session.SessionId} paired"
                + (string.IsNullOrEmpty(session.IdeKey) ? "." : $" (idekey={session.IdeKey})."));
            if (string.IsNullOrEmpty(session.IdeKey))
            {
                this._logger.Info("CLI_XDebugProxySessionPaired", session.SessionId);
            }
            else
            {
                this._logger.Info("CLI_XDebugProxySessionPairedWithKey", session.SessionId, session.IdeKey);
            }
            this.RaiseSessionPaired(session);

            Task run = this.RunSessionAsync(session, this._shutdownCts.Token);
            this._sessionTasks[session.SessionId] = run;
        }

        private async Task RunSessionAsync(DebugSession session, CancellationToken cancellationToken)
        {
            try
            {
                await session.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.Log($"Session {session.SessionId} ended with error: {ex.Message}");
                this._logger.ForSession(session.SessionId).Error("CLI_XDebugProxySessionError", ex.Message);
            }
            finally
            {
                this._sessions.TryRemove(session.SessionId, out _);
                this._sessionTasks.TryRemove(session.SessionId, out Task? _);
                this.Log($"Session {session.SessionId} disconnected.");
                this._logger.Info("CLI_XDebugProxySessionDisconnected", session.SessionId);
                this.RaiseSessionDisconnected(session);
                session.Dispose();
                this.TryPair();
            }
        }

        private PathMapper CreatePathMapper()
        {
            string phpRoot = FirstNonEmpty(
                this._config.PhpOutputRoot,
                this._config.SourceMapDirectory,
                this._sourceMapStore.RootDirectory);
            string tyhpRoot = FirstNonEmpty(this._config.TyhpSourceRoot, phpRoot);
            return new PathMapper(tyhpRoot, phpRoot);
        }

        private void SchedulePairingTimeout(PendingConnection pending, bool isIde)
        {
            TimeSpan? timeout = this._config.PairingTimeout;
            if (timeout is not TimeSpan duration || duration <= TimeSpan.Zero)
            {
                return;
            }

            this.TrackBackground(this.WaitForPairingTimeoutAsync(pending, isIde, duration));
        }

        private async Task WaitForPairingTimeoutAsync(PendingConnection pending, bool isIde, TimeSpan timeout)
        {
            try
            {
                await Task.Delay(timeout, pending.TimeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool removed;
            lock (this._gate)
            {
                removed = isIde
                    ? this._pendingIdes.Remove((PendingIdeConnection)pending)
                    : this._pendingXdebugs.Remove((PendingXdebugConnection)pending);
            }

            if (!removed)
            {
                return;
            }

            string side = isIde ? "IDE" : "XDebug";
            this.RaiseRejected(side, pending.Client, $"pairing timeout ({timeout.TotalSeconds:0}s)");
            TryClose(pending.Client);
            pending.DisposeTimeout();
        }

        private void ClosePendingConnections()
        {
            List<PendingConnection> pending;
            List<TcpClient> handshakes;
            lock (this._gate)
            {
                pending = [.. this._pendingIdes, .. this._pendingXdebugs];
                this._pendingIdes.Clear();
                this._pendingXdebugs.Clear();
                handshakes = [.. this._handshakeClients];
                this._handshakeClients.Clear();
                this._xdebugHandshakes = 0;
            }

            foreach (PendingConnection connection in pending)
            {
                connection.CancelTimeout();
                TryClose(connection.Client);
                connection.DisposeTimeout();
            }

            foreach (TcpClient handshake in handshakes)
            {
                TryClose(handshake);
            }
        }

        private void RaiseSessionPaired(DebugSession session)
        {
            try
            {
                this.SessionPaired?.Invoke(session);
            }
            catch
            {
            }
        }

        private void RaiseSessionDisconnected(DebugSession session)
        {
            try
            {
                this.SessionDisconnected?.Invoke(session);
            }
            catch
            {
            }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return ".";
        }

        private abstract class PendingConnection
        {
            protected PendingConnection(TcpClient client)
            {
                this.Client = client;
                this.TimeoutCts = new CancellationTokenSource();
            }

            public TcpClient Client { get; }

            public CancellationTokenSource TimeoutCts { get; }

            public void CancelTimeout()
            {
                try
                {
                    this.TimeoutCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void DisposeTimeout()
            {
                this.TimeoutCts.Dispose();
            }
        }

        private sealed class PendingIdeConnection : PendingConnection
        {
            public PendingIdeConnection(TcpClient client, string? ideKey = null)
                : base(client)
            {
                this.IdeKey = ideKey;
            }

            /// <summary>
            /// Optional ident if an IDE handshake ever supplies one. Unused today because
            /// debug adapters send nothing until they receive <c>&lt;init&gt;</c>.
            /// </summary>
            public string? IdeKey { get; }
        }

        private sealed class PendingXdebugConnection : PendingConnection
        {
            public PendingXdebugConnection(TcpClient client, DbgpResponse initPacket, string? ideKey)
                : base(client)
            {
                this.InitPacket = initPacket;
                this.IdeKey = ideKey;
            }

            public DbgpResponse InitPacket { get; }

            public string? IdeKey { get; }
        }
    }
}
