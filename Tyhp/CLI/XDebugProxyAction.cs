using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.XDebugProxy;
using Tyhp.XDebugProxy.Config;
using Tyhp.XDebugProxy.Network;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.CLI
{
    /// <summary>
    /// Starts the XDebug proxy as a long-running CLI action.
    /// </summary>
    public sealed class XDebugProxyAction : ActionRunnerBase
    {
        private readonly Project _project;
        private readonly TaskCompletionSource _listeningTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public XDebugProxyAction(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        /// <summary>Completes after both listeners are bound, or faults if startup fails.</summary>
        internal Task WhenListening => this._listeningTcs.Task;

        internal int BoundIdePort { get; private set; }

        internal int BoundXDebugPort { get; private set; }

        internal bool WarnedNoSourceMaps { get; private set; }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.RunAsync(cancellationToken).GetAwaiter().GetResult();
            return null;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var config = this._project.XDebugProxy;
            if (!this.TryValidateAndResolve(config))
            {
                this._listeningTcs.TrySetCanceled(cancellationToken);
                return;
            }

            SourceMapStore? store = null;
            ProxyServer? server = null;
            Task? start = null;
            int shuttingDownPrinted = 0;
            int stoppedPrinted = 0;
            try
            {
                store = new SourceMapStore(
                    config.SourceMapDirectory!,
                    explicitMapPaths: null,
                    onWarning: this.OnSourceMapWarning)
                {
                    AutoReload = config.AutoReloadSourceMaps,
                };
                store.LoadAll();

                if (store.LoadedMaps.Count == 0)
                {
                    this.WarnedNoSourceMaps = true;
                    if (!this._project.BeQuiet)
                    {
                        Message.Warn("CLI_XDebugProxyNoSourceMaps", config.SourceMapDirectory!);
                    }
                }

                var logger = new ProxyLogger(config.LogLevel);
                server = new ProxyServer(config, store, this.OnProxyLog, logger);
                server.ConnectionRejected += this.OnConnectionRejected;

                start = server.StartAsync(cancellationToken);
                await server.WhenListening.ConfigureAwait(false);

                this.BoundIdePort = server.BoundIdePort;
                this.BoundXDebugPort = server.BoundXDebugPort;
                this._listeningTcs.TrySetResult();

                if (!this._project.BeQuiet)
                {
                    this.PrintStartupBanner(config, store.LoadedMaps.Count);
                }

                using (cancellationToken.Register(() => PrintShuttingDownOnce()))
                {
                    // Cleanup (closing sessions/listeners) happens inside `start` via
                    // ProxyServer.StopCoreAsync; only announce "stopped" once that has actually
                    // finished, not the instant cancellation is requested.
                    await start.ConfigureAwait(false);
                }

                PrintStoppedOnce();

                Environment.ExitCode = (int)ExitCode.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this._listeningTcs.TrySetCanceled(cancellationToken);
                PrintShuttingDownOnce();
                PrintStoppedOnce();
                Environment.ExitCode = (int)ExitCode.Success;
            }
            catch (Exception ex) when (IsPortInUse(ex))
            {
                this._listeningTcs.TrySetException(ex);
                int failedPort = InferFailedPort(server, config);
                EmitError(MessageCode.ProxyPortInUse, failedPort);
                string flag = server is not null && server.BoundIdePort > 0
                    ? "xdebug-port"
                    : "ide-port";
                Message.Error("CLI_XDebugProxyPortInUseHint", flag);
                Environment.ExitCode = (int)ExitCode.GenericError;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this._listeningTcs.TrySetException(ex);
                EmitError(MessageCode.ProxyUnknownError, ex.Message);
                Environment.ExitCode = (int)ExitCode.GenericError;
            }
            finally
            {
                if (start is not null)
                {
                    try
                    {
                        await start.ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                    }
                }

                if (server is not null)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }

                store?.Dispose();

                if (!this._listeningTcs.Task.IsCompleted)
                {
                    this._listeningTcs.TrySetCanceled();
                }
            }

            void PrintShuttingDownOnce()
            {
                if (Interlocked.Exchange(ref shuttingDownPrinted, 1) != 0)
                {
                    return;
                }

                if (this._project.BeQuiet)
                {
                    return;
                }

                int sessions = server?.ActiveSessionCount ?? 0;
                Message.Info("CLI_XDebugProxyShuttingDown", sessions);
            }

            void PrintStoppedOnce()
            {
                if (Interlocked.Exchange(ref stoppedPrinted, 1) != 0)
                {
                    return;
                }

                if (this._project.BeQuiet)
                {
                    return;
                }

                Message.Info("CLI_XDebugProxyStopped");
            }
        }

        private void PrintStartupBanner(XDebugProxyConfig config, int sourcemapCount)
        {
            Message.Info("CLI_XDebugProxyStarted");
            Message.Display("CLI_XDebugProxyStartedIdePort", this.BoundIdePort);
            Message.Display("CLI_XDebugProxyStartedXdebugPort", this.BoundXDebugPort);
            Message.Display(
                "CLI_XDebugProxyStartedSourcemaps",
                sourcemapCount,
                config.SourceMapDirectory ?? string.Empty);
            Message.Display(
                "CLI_XDebugProxyStartedSourceRoot",
                config.TyhpSourceRoot ?? string.Empty);
            string ideKey = String.IsNullOrEmpty(config.IdeKey)
                ? Message.Localize("CLI_XDebugProxyIdeKeyAny")
                : config.IdeKey;
            Message.Display("CLI_XDebugProxyStartedIdeKey", ideKey);
        }

        private bool TryValidateAndResolve(XDebugProxyConfig config)
        {
            if (!this.TryValidatePort("ide-port", "xdebugProxy:idePort", config.IdeListenPort)
                || !this.TryValidatePort("xdebug-port", "xdebugProxy:xdebugPort", config.XDebugListenPort))
            {
                return false;
            }

            if (!IsValidListenAddress(config.IdeListenAddress))
            {
                Message.Error("CLI_XDebugProxyInvalidAddress", config.IdeListenAddress);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            if (!IsValidListenAddress(config.XDebugListenAddress))
            {
                Message.Error("CLI_XDebugProxyInvalidAddress", config.XDebugListenAddress);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            var maxSessionsRaw = this._project.GetConfigValue("xdebugProxy:maxSessions");
            if (!String.IsNullOrWhiteSpace(maxSessionsRaw)
                && (!Int32.TryParse(
                        maxSessionsRaw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int parsedMax)
                    || parsedMax < 1))
            {
                Message.Error("CLI_XDebugProxyInvalidMaxSessions", maxSessionsRaw.Trim());
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            if (config.MaxSessions < 1)
            {
                Message.Error("CLI_XDebugProxyInvalidMaxSessions", config.MaxSessions);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            string projectPath = PathCanonicalizer.GetCanonicalFullPath(this._project.GetProjectPath());
            string outputPath = BuildOutputCleaner.ResolveOutputDirectory(
                projectPath,
                this._project.Output.Path);

            config.TyhpSourceRoot = ResolveDirectory(config.TyhpSourceRoot, projectPath, projectPath);
            config.PhpOutputRoot = ResolveDirectory(config.PhpOutputRoot, outputPath, projectPath);
            config.SourceMapDirectory = ResolveDirectory(
                config.SourceMapDirectory,
                outputPath,
                projectPath);

            bool sourceMapDirExplicit =
                !String.IsNullOrWhiteSpace(this._project.GetConfigValue("sourcemap-dir"))
                || !String.IsNullOrWhiteSpace(this._project.GetConfigValue("xdebugProxy:sourceMapDir"));

            if (sourceMapDirExplicit && !Directory.Exists(config.SourceMapDirectory))
            {
                Message.Error("CLI_XDebugProxySourceMapDirMissing", config.SourceMapDirectory!);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            return true;
        }

        private bool TryValidatePort(string cliKey, string jsonKey, int resolvedPort)
        {
            string? raw = this._project.GetConfigValue(cliKey)
                ?? this._project.GetConfigValue(jsonKey);

            if (!String.IsNullOrWhiteSpace(raw)
                && !Int32.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                Message.Error("CLI_XDebugProxyInvalidPort", raw.Trim());
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            if (resolvedPort < 0 || resolvedPort > 65535)
            {
                Message.Error(
                    "CLI_XDebugProxyInvalidPort",
                    raw?.Trim() ?? resolvedPort.ToString(CultureInfo.InvariantCulture));
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            return true;
        }

        private void OnSourceMapWarning(SourceMapLoadWarning warning)
        {
            switch (warning.Kind)
            {
                case SourceMapLoadWarningKind.MapFileMalformed:
                case SourceMapLoadWarningKind.MapFileUnreadable:
                    EmitWarning(
                        MessageCode.ProxySourceMapParseError,
                        warning.Path ?? warning.Message,
                        warning.Message);
                    break;
                case SourceMapLoadWarningKind.MapFileMissing:
                    EmitWarning(
                        MessageCode.ProxySourceMapNotFound,
                        warning.Path ?? warning.Message);
                    break;
                case SourceMapLoadWarningKind.RootDirectoryMissing:
                    // Empty/missing default output dir is the CLI no-sourcemaps warning.
                    break;
            }
        }

        private void OnProxyLog(string message)
        {
            if (message.Contains("translation:", StringComparison.Ordinal))
            {
                EmitWarning(MessageCode.ProxyTranslationError, message);
            }
            else if (message.Contains("malformed", StringComparison.OrdinalIgnoreCase))
            {
                EmitWarning(MessageCode.ProxyInvalidDbgpMessage, message);
            }
        }

        private void OnConnectionRejected(string side, string reason)
        {
            if (reason.Contains("pairing timeout", StringComparison.OrdinalIgnoreCase))
            {
                EmitWarning(MessageCode.ProxySessionPairingTimeout, side);
            }
        }

        private static void EmitError(MessageCode code, params object[] args)
        {
            Message.TyhpError("xdebug_proxy", 0, 0, (int)code, args);
        }

        private static void EmitWarning(MessageCode code, params object[] args)
        {
            Message.TyhpWarn("xdebug_proxy", 0, 0, (int)code, args);
        }

        private static int InferFailedPort(ProxyServer? server, XDebugProxyConfig config)
        {
            if (server is not null && server.BoundIdePort > 0)
            {
                return config.XDebugListenPort;
            }

            return config.IdeListenPort;
        }

        private static bool IsPortInUse(Exception ex)
        {
            for (Exception? current = ex; current is not null; current = current.InnerException)
            {
                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (IsPortInUse(inner))
                        {
                            return true;
                        }
                    }
                }

                if (current is SocketException socket
                    && socket.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidListenAddress(string address)
        {
            if (String.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            if (String.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(address, out _);
        }

        private static string ResolveDirectory(string? configured, string fallback, string projectPath)
        {
            string path = String.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
            if (Path.IsPathRooted(path))
            {
                return PathCanonicalizer.GetCanonicalFullPath(path);
            }

            return PathCanonicalizer.GetCanonicalFullPath(Path.Combine(projectPath, path));
        }
    }
}
