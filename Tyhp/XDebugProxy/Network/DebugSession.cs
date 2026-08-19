using System.Net.Sockets;
using System.Xml;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.XDebugProxy.Network
{
    /// <summary>
    /// Bidirectional DBGp relay for one paired IDE + XDebug connection.
    /// One <see cref="DbgpMessageTranslator"/> is kept for the session lifetime.
    /// </summary>
    public sealed class DebugSession : IDisposable
    {
        private const string DirectionIdeToXdebug = "IDE→XDebug";
        private const string DirectionXdebugToIde = "XDebug→IDE";

        private readonly TcpConnectionHandler _ide;
        private readonly TcpConnectionHandler _xdebug;
        private readonly DbgpResponse _initPacket;
        private readonly Action<string>? _onLog;
        private readonly ProxyLogger? _logger;
        private int _disposed;

        public DebugSession(
            string sessionId,
            TcpClient ideClient,
            TcpClient xdebugClient,
            DbgpMessageTranslator translator,
            DbgpResponse initPacket,
            Action<string>? onLog = null,
            ProxyLogger? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentNullException.ThrowIfNull(ideClient);
            ArgumentNullException.ThrowIfNull(xdebugClient);
            ArgumentNullException.ThrowIfNull(translator);
            ArgumentNullException.ThrowIfNull(initPacket);

            this.SessionId = sessionId;
            this.IdeClient = ideClient;
            this.XDebugClient = xdebugClient;
            this.Translator = translator;
            this.IdeKey = initPacket.GetAttribute("idekey");
            this._initPacket = initPacket;
            this._onLog = onLog;
            this._logger = logger;
            this._ide = new TcpConnectionHandler(ideClient);
            this._xdebug = new TcpConnectionHandler(xdebugClient);
        }

        public string SessionId { get; }

        public TcpClient IdeClient { get; }

        public TcpClient XDebugClient { get; }

        public DbgpMessageTranslator Translator { get; }

        /// <summary><c>idekey</c> from the XDebug <c>&lt;init&gt;</c> packet, if present.</summary>
        public string? IdeKey { get; }

        /// <summary>
        /// Forward the translated <c>&lt;init&gt;</c> packet, then relay both directions until
        /// either side disconnects or <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(this._disposed != 0, this);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                this.Translator.TranslateXDebugToIde(this._initPacket);
                this.LogRelayedResponse(DirectionXdebugToIde, this._initPacket);
                await this._ide.WriteResponseAsync(this._initPacket, linked.Token).ConfigureAwait(false);

                Task ideToXdebug = this.RelayIdeToXDebugAsync(linked.Token);
                Task xdebugToIde = this.RelayXDebugToIdeAsync(linked.Token);
                await Task.WhenAny(ideToXdebug, xdebugToIde).ConfigureAwait(false);
                linked.Cancel();
                this.CloseConnections();
                await Task.WhenAll(Observe(ideToXdebug), Observe(xdebugToIde)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                this.CloseConnections();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            this.CloseConnections();
            try
            {
                this.IdeClient.Dispose();
            }
            catch
            {
            }

            try
            {
                this.XDebugClient.Dispose();
            }
            catch
            {
            }

            GC.SuppressFinalize(this);
        }

        internal void CloseConnections()
        {
            TryClose(this.IdeClient);
            TryClose(this.XDebugClient);
        }

        private async Task RelayIdeToXDebugAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DbgpCommand command;
                try
                {
                    command = await this._ide.ReadCommandAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (this.ShouldContinueAfterReadError(ex, "IDE"))
                    {
                        continue;
                    }

                    return;
                }

                try
                {
                    DbgpResponse? intercepted = this.Translator.InterceptCommand(command);
                    if (intercepted is not null)
                    {
                        this.LogRelayedCommand("IDE→Proxy", command, beforePath: command.Filename, beforeLine: command.LineNumber);
                        await this._ide.WriteResponseAsync(intercepted, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    string? beforePath = command.Filename;
                    string? beforeLine = command.LineNumber;
                    this.Translator.TranslateIdeToXDebug(command);
                    this.LogRelayedCommand(DirectionIdeToXdebug, command, beforePath, beforeLine);
                    await this._xdebug.WriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (this.ShouldStopAfterWriteError(ex, "IDE→XDebug"))
                    {
                        return;
                    }
                }
            }
        }

        private async Task RelayXDebugToIdeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DbgpResponse response;
                try
                {
                    response = await this._xdebug.ReadResponseAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (this.ShouldContinueAfterReadError(ex, "XDebug"))
                    {
                        continue;
                    }

                    return;
                }

                try
                {
                    this.Translator.TranslateXDebugToIde(response);
                    this.LogRelayedResponse(DirectionXdebugToIde, response);
                    await this._ide.WriteResponseAsync(response, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (this.ShouldStopAfterWriteError(ex, "XDebug→IDE"))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// <see langword="true"/> to skip a bad message and keep relaying;
        /// <see langword="false"/> to stop the relay (disconnect, cancel, or unexpected error).
        /// </summary>
        private bool ShouldContinueAfterReadError(Exception ex, string side)
        {
            if (ex is OperationCanceledException)
            {
                return false;
            }

            if (IsSkippableParseError(ex))
            {
                this.Log($"Session {this.SessionId}: skipping malformed {side} message: {ex.Message}");
                this._logger?.Warn("CLI_XDebugProxyMalformedDbgp", side, ex.Message);
                return true;
            }

            if (IsConnectionLoss(ex))
            {
                this.Log($"Session {this.SessionId}: {side} connection lost: {ex.Message}");
                this._logger?.Info("CLI_XDebugProxyConnectionLost", side, ex.Message);
                return false;
            }

            this.Log($"Session {this.SessionId}: {side} relay error: {ex.Message}");
            this._logger?.Error("CLI_XDebugProxyConnectionFailed", side, ex.Message);
            return false;
        }

        private bool ShouldStopAfterWriteError(Exception ex, string direction)
        {
            if (ex is OperationCanceledException)
            {
                return true;
            }

            if (IsConnectionLoss(ex))
            {
                this.Log($"Session {this.SessionId}: {direction} connection lost: {ex.Message}");
                this._logger?.Info("CLI_XDebugProxyConnectionLost", direction, ex.Message);
                return true;
            }

            this.Log($"Session {this.SessionId}: {direction} error: {ex.Message}");
            this._logger?.Error("CLI_XDebugProxyConnectionFailed", direction, ex.Message);
            return false;
        }

        private static bool IsConnectionLoss(Exception ex)
        {
            for (Exception? current = ex; current is not null; current = current.InnerException)
            {
                if (current is IOException or SocketException or ObjectDisposedException)
                {
                    return true;
                }

                if (current is DbgpProtocolException
                    && current.Message.Contains("end of stream", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSkippableParseError(Exception ex)
        {
            if (IsConnectionLoss(ex))
            {
                return false;
            }

            return ex is XmlException or DbgpProtocolException
                || ex.InnerException is XmlException;
        }

        private static async Task Observe(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
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
        }

        private void LogRelayedCommand(
            string direction,
            DbgpCommand command,
            string? beforePath,
            string? beforeLine)
        {
            if (this._logger is null || !this._logger.IsEnabled(ProxyLogLevel.Debug))
            {
                return;
            }

            string paths = FormatPathChange(beforePath, beforeLine, command.Filename, command.LineNumber);
            this._logger.Debug($"{direction} {command.CommandName}{paths}");
            if (!string.IsNullOrEmpty(command.RawText))
            {
                this._logger.Debug($"{direction} {ProxyLogger.Truncate(command.RawText)}");
            }
        }

        private void LogRelayedResponse(string direction, DbgpResponse response)
        {
            if (this._logger is null || !this._logger.IsEnabled(ProxyLogLevel.Debug))
            {
                return;
            }

            string commandName = response.Command ?? response.RootLocalName;
            string? filename = response.GetAttribute("filename") ?? response.GetAttribute("fileuri");
            string? lineno = response.GetAttribute("lineno");
            string paths = FormatPathChange(beforePath: null, beforeLine: null, filename, lineno);
            this._logger.Debug($"{direction} {commandName}{paths}");
            this._logger.Debug($"{direction} {ProxyLogger.Truncate(response.RootElement.ToString())}");
        }

        private static string FormatPathChange(
            string? beforePath,
            string? beforeLine,
            string? afterPath,
            string? afterLine)
        {
            bool hasAfter = !string.IsNullOrEmpty(afterPath) || !string.IsNullOrEmpty(afterLine);
            if (!hasAfter)
            {
                return string.Empty;
            }

            string after = $" {afterPath}:{afterLine}";
            bool changed = !string.Equals(beforePath, afterPath, StringComparison.Ordinal)
                || !string.Equals(beforeLine, afterLine, StringComparison.Ordinal);
            if (changed && (!string.IsNullOrEmpty(beforePath) || !string.IsNullOrEmpty(beforeLine)))
            {
                return $" {beforePath}:{beforeLine} →{after}";
            }

            return after;
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
        }
    }
}
