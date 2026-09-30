using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Tyhp.XDebugProxy.Network
{
    public sealed partial class ProxyServer
    {
        private void StartListeners()
        {
            try
            {
                this._ideListener = CreateListener(this._config.IdeListenAddress, this._config.IdeListenPort);
                this._ideListener.Start();
                this.BoundIdePort = ((IPEndPoint)this._ideListener.LocalEndpoint).Port;

                this._xdebugListener = CreateListener(this._config.XDebugListenAddress, this._config.XDebugListenPort);
                this._xdebugListener.Start();
                this.BoundXDebugPort = ((IPEndPoint)this._xdebugListener.LocalEndpoint).Port;
            }
            catch
            {
                this.StopListeners();
                throw;
            }
        }

        private async Task AcceptIdeLoopAsync(CancellationToken cancellationToken)
        {
            TcpListener listener = this._ideListener
                ?? throw new InvalidOperationException("IDE listener is not started.");

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
                {
                    if (ShouldStopAcceptLoop(ex, cancellationToken.IsCancellationRequested))
                    {
                        break;
                    }

                    this.Log($"IDE accept attempt failed; still listening: {ex.Message}");
                    this._logger.Warn("CLI_XDebugProxyAcceptFailed", "IDE", ex.Message);
                    continue;
                }

                this.OnIdeAccepted(client);
            }
        }

        private async Task AcceptXdebugLoopAsync(CancellationToken cancellationToken)
        {
            TcpListener listener = this._xdebugListener
                ?? throw new InvalidOperationException("XDebug listener is not started.");

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
                {
                    if (ShouldStopAcceptLoop(ex, cancellationToken.IsCancellationRequested))
                    {
                        break;
                    }

                    this.Log($"XDebug accept attempt failed; still listening: {ex.Message}");
                    this._logger.Warn("CLI_XDebugProxyAcceptFailed", "XDebug", ex.Message);
                    continue;
                }

                this.TrackBackground(this.OnXdebugAcceptedAsync(client, cancellationToken));
            }
        }

        private void StopListeners()
        {
            StopListener(ref this._ideListener);
            StopListener(ref this._xdebugListener);
        }

        private async Task StopCoreAsync()
        {
            if (Interlocked.Exchange(ref this._stopping, 1) != 0)
            {
                await this._stoppedTcs.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                this.StopListeners();
                this.ClosePendingConnections();
                foreach (DebugSession session in this._sessions.Values)
                {
                    session.CloseConnections();
                }

                await WhenAllTracked(this._backgroundTasks).ConfigureAwait(false);
                await WhenAllTracked(this._sessionTasks).ConfigureAwait(false);
            }
            finally
            {
                this._stoppedTcs.TrySetResult();
            }
        }

        private void DisposeManaged()
        {
            this._shutdownCts.Dispose();
        }

        private void TrackBackground(Task task)
        {
            int id = Interlocked.Increment(ref this._backgroundSerial);
            this._backgroundTasks[id] = task;
            _ = task.ContinueWith(
                _ => this._backgroundTasks.TryRemove(id, out Task? _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task WhenAllTracked(ConcurrentDictionary<string, Task> tasks)
        {
            Task[] snapshot = [.. tasks.Values];
            if (snapshot.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        private static async Task WhenAllTracked(ConcurrentDictionary<int, Task> tasks)
        {
            Task[] snapshot = [.. tasks.Values];
            if (snapshot.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        private static TcpListener CreateListener(string address, int port)
        {
            var listener = new TcpListener(ParseAddress(address), port);
            listener.Server.NoDelay = true;
            return listener;
        }

        private static IPAddress ParseAddress(string address)
        {
            if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback;
            }

            return IPAddress.Parse(address);
        }

        private static void ConfigureClient(TcpClient client)
        {
            client.NoDelay = true;
        }

        private static void StopListener(ref TcpListener? listener)
        {
            TcpListener? local = Interlocked.Exchange(ref listener, null);
            if (local is null)
            {
                return;
            }

            try
            {
                local.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        /// <summary>
        /// <see langword="true"/> when an <c>AcceptTcpClientAsync</c> failure means the
        /// listener itself is gone (cancellation, <see cref="StopListeners"/> having disposed
        /// it) and the accept loop must stop. A bare <see cref="SocketException"/> while the
        /// server is <em>not</em> shutting down affects only that one connection attempt (e.g.
        /// a peer resetting the connection before the accept handshake completes) — the
        /// listener socket is still bound and must keep accepting, otherwise a single bad
        /// connection attempt would silently and permanently stop the proxy from accepting any
        /// further IDE/XDebug sessions.
        /// </summary>
        internal static bool ShouldStopAcceptLoop(Exception ex, bool cancellationRequested)
        {
            if (cancellationRequested)
            {
                return true;
            }

            return ex is not SocketException;
        }

        private static void TryClose(TcpClient client)
        {
            try
            {
                client.Close();
            }
            catch
            {
            }

            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        private void RaiseAccepted(string side, TcpClient client)
        {
            string remote = DescribeRemote(client);
            this.Log($"{side} connected from {remote}.");
            this._logger.Info("CLI_XDebugProxyConnectionAccepted", side, remote);
            try
            {
                this.ConnectionAccepted?.Invoke(side, remote);
            }
            catch
            {
            }
        }

        private void RaiseRejected(string side, TcpClient client, string reason)
        {
            string remote = DescribeRemote(client);
            this.Log($"{side} connection from {remote} rejected: {reason}.");
            this._logger.Warn("CLI_XDebugProxyConnectionRejected", side, remote, reason);
            try
            {
                this.ConnectionRejected?.Invoke(side, reason);
            }
            catch
            {
            }
        }

        private static string DescribeRemote(TcpClient client)
        {
            try
            {
                return client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private void Log(string message)
        {
            try
            {
                this._onLog?.Invoke(message);
            }
            catch
            {
            }

            this._logger.Debug(message);
        }
    }
}
