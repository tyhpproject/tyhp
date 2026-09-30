namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.CLI;
    using Tyhp.Domain.Services;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Configuration;
    using Tyhp.LanguageServer.Handlers;
    using Tyhp.LanguageServer.Workspace;

    /// <summary>
    /// Language Server Protocol target. Feature handlers are additional
    /// <c>partial class</c> files with <see cref="JsonRpcMethodAttribute"/> methods;
    /// StreamJsonRpc discovers them through a single <see cref="JsonRpc.AddLocalRpcTarget(object)"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Grace period for <see cref="DisposeWithForcedExitWatchdog"/> to let
        /// <see cref="JsonRpc.Completion"/> unwind on its own after <see cref="JsonRpc.Dispose"/>
        /// before forcing the process to exit. Only relevant to real stdio (see
        /// <see cref="_allowForcedProcessExit"/>) — see that method for why disposal alone is
        /// not always enough.
        /// </summary>
        private static readonly TimeSpan ExitForceProcessExitGracePeriod = TimeSpan.FromSeconds(3);

        private readonly JsonRpc _jsonRpc;
        private readonly ServerConfiguration _configuration;
        private readonly WorkspaceManager _workspace;
        private readonly CompilationService _compilation;
        private readonly DiagnosticsPublisher _diagnosticsPublisher;
        private readonly AnalysisService _analysis;
        private readonly IncrementalAnalyzer _incrementalAnalyzer;
        private readonly SymbolFinder _symbolFinder;
        private readonly bool _allowForcedProcessExit;
        private bool _shutdownRequested;

        internal TyhpLanguageServer(
            JsonRpc jsonRpc,
            ServerConfiguration configuration,
            bool allowForcedProcessExit = false,
            WorkspaceManager? workspaceManager = null)
        {
            this._jsonRpc = jsonRpc ?? throw new ArgumentNullException(nameof(jsonRpc));
            this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this._allowForcedProcessExit = allowForcedProcessExit;
            this._workspace = workspaceManager ?? new WorkspaceManager();
            this._compilation = new CompilationService();
            this._diagnosticsPublisher = new DiagnosticsPublisher(jsonRpc, configuration);
            this._symbolFinder = new SymbolFinder();
            this._analysis = new AnalysisService(
                this._workspace,
                this._compilation,
                this._diagnosticsPublisher,
                configuration,
                this._symbolFinder)
            {
                Log = this.LogMessage,
            };
            this._incrementalAnalyzer = new IncrementalAnalyzer(this._analysis, configuration);
        }

        /// <summary>Tracked documents for this server instance.</summary>
        internal WorkspaceManager Workspace => this._workspace;

        /// <summary>Document analysis coordinator for tests and later feature handlers.</summary>
        internal AnalysisService Analysis => this._analysis;

        /// <summary>Symbol finder for later feature handlers (definition, hover, references).</summary>
        internal SymbolFinder SymbolFinder => this._symbolFinder;

        /// <summary>
        /// Runs the language server over stdin/stdout until the client disconnects
        /// or sends <c>exit</c>.
        /// </summary>
        public static Task RunAsync(CancellationToken cancellationToken)
            => RunAsync(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                new ServerConfiguration(),
                cancellationToken,
                onListening: null,
                allowForcedProcessExit: true);

        /// <summary>
        /// Runs the language server over the given streams. Used by
        /// <see cref="LanguageServerAction"/> and tests.
        /// </summary>
        /// <param name="allowForcedProcessExit">
        /// Whether <see cref="Exit"/> may call <see cref="Environment.Exit(int)"/> as a last
        /// resort when <paramref name="input"/>/<paramref name="output"/> do not unblock after
        /// disposal (real stdio). Tests pass cancellable in-memory streams and must leave this
        /// <see langword="false"/> so a slow CI run cannot tear down the test host process.
        /// </param>
        internal static async Task RunAsync(
            Stream input,
            Stream output,
            ServerConfiguration configuration,
            CancellationToken cancellationToken,
            Action? onListening = null,
            bool allowForcedProcessExit = false,
            Action<TyhpLanguageServer>? onServerCreated = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(configuration);

            var formatter = new JsonMessageFormatter();
            var handler = new HeaderDelimitedMessageHandler(output, input, formatter);
            using var jsonRpc = new JsonRpc(handler);
            var server = new TyhpLanguageServer(jsonRpc, configuration, allowForcedProcessExit);
            onServerCreated?.Invoke(server);
            jsonRpc.AddLocalRpcTarget(server);

            // Host shutdown (Ctrl+C, or TyhpHostedService.StopAsync) cancels this token the same
            // way `exit` disposes the RPC below: real stdio's pending read may not unblock from
            // disposal alone (see DisposeWithForcedExitWatchdog), so without this watchdog Ctrl+C
            // would sit for the full default 30s host shutdown timeout instead of exiting promptly.
            using var cancelRegistration = cancellationToken.Register(state =>
            {
                DisposeWithForcedExitWatchdog(
                    (JsonRpc)state!,
                    allowForcedProcessExit,
                    (int)Tyhp.Domain.Enums.ExitCode.Success);
            }, jsonRpc);

            jsonRpc.StartListening();
            onListening?.Invoke();

            try
            {
                await jsonRpc.Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ConnectionLostException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
                // FullDuplexStream / PipeReader can throw "Reading is not allowed after
                // reader was completed" when the peer disposes during shutdown.
            }
            finally
            {
                server._incrementalAnalyzer.Dispose();
                server._analysis.Dispose();
                server._compilation.Dispose();
                server.Workspace.Dispose();
            }
        }

        /// <summary>
        /// LSP <c>initialize</c> — capability negotiation.
        /// </summary>
        [JsonRpcMethod(Methods.InitializeName, UseSingleObjectParameterDeserialization = true)]
        public InitializeResult Initialize(InitializeParams arg, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(arg);

            // `rootUri` supersedes the deprecated `rootPath`, but some clients still only send
            // the latter (plain filesystem path, not a URI) — fall back to it so the workspace
            // root is still captured for the Phase 3 project scan.
#pragma warning disable CS0618 // RootPath is deprecated in favor of RootUri; intentional fallback.
            string? resolvedRoot = arg.RootUri is not null
                ? (arg.RootUri.IsFile ? arg.RootUri.LocalPath : arg.RootUri.OriginalString)
                : (string.IsNullOrEmpty(arg.RootPath) ? null : arg.RootPath);
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(this._configuration.TyhpProjectPath)
                && resolvedRoot is not null)
            {
                this._configuration.TyhpProjectPath = resolvedRoot;
            }

            this._workspace.WorkspaceRoot = resolvedRoot ?? this._configuration.TyhpProjectPath;

            return new InitializeResult
            {
                Capabilities = CapabilityRegistration.Create(),
            };
        }

        /// <summary>
        /// LSP <c>initialized</c> — post-initialize hook.
        /// </summary>
        [JsonRpcMethod(Methods.InitializedName, UseSingleObjectParameterDeserialization = true)]
        public void Initialized(InitializedParams arg)
        {
            _ = arg;
            _ = this.OnInitializedAsync();
        }

        /// <summary>
        /// LSP <c>window/logMessage</c> — server-side log line for the client.
        /// </summary>
        internal void LogMessage(MessageType type, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            _ = this.NotifyLogMessageAsync(type, message);
        }

        /// <summary>
        /// LSP <c>shutdown</c> — prepare to exit; the process stays up until <c>exit</c>.
        /// </summary>
        [JsonRpcMethod(Methods.ShutdownName)]
        public object? Shutdown(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this._shutdownRequested = true;
            return null;
        }

        /// <summary>
        /// LSP <c>exit</c> — disconnect JSON-RPC so <see cref="RunAsync(Stream, Stream, ServerConfiguration, CancellationToken, Action?)"/> returns.
        /// </summary>
        [JsonRpcMethod(Methods.ExitName)]
        public void Exit()
        {
            var exitCode = this._shutdownRequested
                ? (int)Tyhp.Domain.Enums.ExitCode.Success
                : (int)Tyhp.Domain.Enums.ExitCode.GenericError;
            Environment.ExitCode = exitCode;

            // Dispose after this handler returns so StreamJsonRpc can finish writing any
            // in-flight response. Environment.Exit is avoided here (as opposed to the watchdog
            // inside DisposeWithForcedExitWatchdog) so tests can shut the server down without
            // killing the test host.
            DisposeWithForcedExitWatchdog(this._jsonRpc, this._allowForcedProcessExit, exitCode);
        }

        private async Task OnInitializedAsync()
        {
            try
            {
                await this._analysis.ScanWorkspaceAsync().ConfigureAwait(false);
                await this._analysis.AnalyzeAllAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogMessage(
                    MessageType.Error,
                    Message.Localize("CLI_LanguageServerWorkspaceScanFailed", ex.GetType().Name, ex.Message));
            }
        }

        private async Task NotifyLogMessageAsync(MessageType type, string message)
        {
            try
            {
                await this._jsonRpc.NotifyWithParameterObjectAsync(
                    Methods.WindowLogMessageName,
                    new LogMessageParams { MessageType = type, Message = message }).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException
                or ConnectionLostException
                or InvalidOperationException
                or IOException)
            {
            }
        }

        /// <summary>
        /// Disposes <paramref name="jsonRpc"/> off the calling thread, then — only when
        /// <paramref name="allowForcedProcessExit"/> is set (real stdio, never test streams) —
        /// force-exits the process with <paramref name="forcedExitCode"/> if
        /// <see cref="JsonRpc.Completion"/> has not resolved on its own within
        /// <see cref="ExitForceProcessExitGracePeriod"/>.
        /// </summary>
        /// <remarks>
        /// Disposing <see cref="JsonRpc"/> cancels pending reads on cancellable in-memory/pipe
        /// test streams (e.g. Nerdbank.Streams), but a blocking read already in flight on the
        /// real <c>Console.OpenStandardInput()</c> stream is not reliably interrupted by
        /// disposal on every platform. Both shutdown triggers — the client's <c>exit</c>
        /// notification and host cancellation (Ctrl+C / <c>TyhpHostedService.StopAsync</c>) —
        /// dispose the same way, so both need this fallback or the process would otherwise hang
        /// (or, for host cancellation, sit for the full default 30s shutdown timeout) whenever
        /// the client keeps stdin open after signalling shutdown.
        /// </remarks>
        private static void DisposeWithForcedExitWatchdog(
            JsonRpc jsonRpc,
            bool allowForcedProcessExit,
            int forcedExitCode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    jsonRpc.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }

                if (!allowForcedProcessExit)
                {
                    return;
                }

                var winner = await Task.WhenAny(
                    jsonRpc.Completion,
                    Task.Delay(ExitForceProcessExitGracePeriod)).ConfigureAwait(false);
                if (winner != jsonRpc.Completion)
                {
                    Environment.Exit(forcedExitCode);
                }
            });
        }

    }
}
