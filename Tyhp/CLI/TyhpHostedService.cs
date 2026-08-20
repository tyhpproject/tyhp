namespace Tyhp.CLI
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Primitives;
    using Tyhp.LanguageServer;

    class TyhpHostedService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IConfiguration _configuration;
        private readonly Tyhp.Config.Project project;
        private readonly IStringLocalizer<TyhpHostedService>? _localizer = null;
        private bool _isLongRunning = false;
        private CancellationTokenSource _actionCancelTokenSource;
        private ActionRunnerBase? _actionRunner;
        private bool disposedValue;
        private string? _pidFilePath;

        /// <summary>
        /// Tracks a long-running action (<c>xdebug_proxy</c>, <c>language_server</c>) started on a
        /// background task instead of inline in <see cref="StartAsync"/>. See the comment at
        /// the <c>xdebug_proxy</c> case for why running it inline would deadlock shutdown.
        /// </summary>
        private Task? _longRunningActionTask;

        public TyhpHostedService(
            ILogger<TyhpHostedService> logger,
            IHostApplicationLifetime appLifetime,
            IConfiguration configuration,
            IStringLocalizer<TyhpHostedService> localizer
        ) {
            this._logger = logger;
            this._appLifetime = appLifetime;
            this._configuration = configuration;
            this._localizer = localizer;

            // Must precede the Project construction below: ConfigChanged() emits configuration
            // warnings through Message, which falls back to printing the raw resource key when no
            // localizer is set.
            Message.SetLocalizer(this._localizer);

            this.project = new Config.Project(this._configuration);

            ChangeToken.OnChange(
                () => this._configuration.GetReloadToken(),
                () => this.project.ConfigChanged()
            );

            this._appLifetime.ApplicationStarted.Register(OnStarted);
            this._appLifetime.ApplicationStopping.Register(OnStopping);
            this._actionCancelTokenSource = new CancellationTokenSource();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // The tokenize / dump-ast debug commands emit machine-readable JSON on stdout, so the
            // banner and their status lines must not pollute stdout. Suppress the banner for them
            // (their status lines are written to stderr instead). Same for lint --format=json|sarif
            // (progress goes to stderr; only the formatted document goes to stdout) and
            // version --json (JSON version payload on stdout).
            var rawAction = this._configuration["*action"] ?? "invalid";
            var versionJson =
                rawAction == Tyhp.Config.Action.version.ToString()
                && this.project.JsonOutput;
            var isMachineOutputAction =
                rawAction == Tyhp.Config.Action.tokenize.ToString()
                || rawAction == Tyhp.Config.Action.dump_ast.ToString()
                || rawAction == Tyhp.Config.Action.language_server.ToString()
                || (rawAction == Tyhp.Config.Action.lint.ToString()
                    && LintAction.UsesMachineReadableOutput(this.project.LintFormat))
                || versionJson;

            // display the cli banner (suppressed for --quiet and machine-readable formats)
            if (!this.project.BeQuiet && !isMachineOutputAction) {
                Message.Banner();
            }

            // Opt-in via --pid-file. Never write tyhp.pid into the working directory by default:
            // language_server and xdebug_proxy can run concurrently (and soon, one LSP per project),
            // and a shared cwd file would clobber other processes and pollute the user's project.
            this._pidFilePath = TryWritePidFile(this.project.PidFile);
            try {
                Tyhp.Config.Action action = Enum.Parse<Tyhp.Config.Action>(this._configuration["*action"] ?? "invalid");

                // Config warnings fire during Project construction (before a lint/build bag exists).
                // Lint/build fold them into DiagnosticBag; other machine-readable actions flush to
                // stderr so stdout stays parseable; remaining actions print to the console.
                var foldsConfigIntoBag =
                    action == Tyhp.Config.Action.lint
                    || action == Tyhp.Config.Action.build;
                if (!foldsConfigIntoBag)
                {
                    if (isMachineOutputAction)
                    {
                        this.project.EmitPendingConfigWarningsToStderr();
                    }
                    else
                    {
                        this.project.EmitPendingConfigWarningsToConsole();
                    }
                }

                switch (action) {
                    case Tyhp.Config.Action.invalid:
                        Message.Error(
                            "CLI_InvalidAction",
                            this._configuration["*raw_action"] ?? Message.Localize("CLI_NoActionSpecified")
                        );
                        Environment.ExitCode = (int)Tyhp.Domain.Enums.ExitCode.InvalidAction;
                        goto case Tyhp.Config.Action.help;
                    case Tyhp.Config.Action.help:
                        Tyhp.Config.DisplayHelp.Execute();
                        // Preserve InvalidAction from the fallthrough case above.
                        if (Environment.ExitCode != (int)Tyhp.Domain.Enums.ExitCode.InvalidAction)
                        {
                            Environment.ExitCode = (int)Tyhp.Domain.Enums.ExitCode.Success;
                        }
                        break;
                    case Tyhp.Config.Action.version:
                        this._actionRunner = new VersionAction(this.project);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;
                    case Tyhp.Config.Action.explain:
                        this._actionRunner = new ExplainAction(this.project);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;
                    case Tyhp.Config.Action.init:
                        this._actionRunner = new InitAction(this.project);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;
                    case Tyhp.Config.Action.composer:
                        this._actionRunner = new ComposerAction(this.project);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;
                    case Tyhp.Config.Action.lint:
                        // lint the project and report errors/warnings, but do not emit/write
                        var lintFormatter = LintAction.CreateFormatter(
                            this.project.LintFormat,
                            this.project.BeQuiet);
                        this._actionRunner = new LintAction(this.project, lintFormatter);
                        var lintResult = this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        if (lintResult != null)
                        {
                            Environment.ExitCode = (int)lintResult.GetExitCode(this.project.Strict);
                        }
                        break;
                    case Tyhp.Config.Action.tokenize:
                        // lex the file(s) and dump the token list as JSON (lexer debugging)
                        this._actionRunner = new TokenizeAction(
                            this.project,
                            this._configuration["out"],
                            this._configuration["mode"]);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;
                    case Tyhp.Config.Action.dump_ast:
                        // parse the file(s) and dump the AST as JSON (parser debugging)
                        this._actionRunner = new DumpAstAction(
                            this.project,
                            this._configuration["out"],
                            this._configuration["mode"]);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;
                    case Tyhp.Config.Action.build:
                        // build the project, generates a sourcemap
                        this._actionRunner = new BuildAction(this.project);
                        var buildResult = this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        if (buildResult != null)
                        {
                            Environment.ExitCode = (int)buildResult.GetExitCode(this.project.Strict);
                        }

                        break;
                    case Tyhp.Config.Action.language_server:
                        // Same Generic Host deadlock as xdebug_proxy: calling
                        // LanguageServerAction.Start() inline would block StartAsync, so
                        // StopAsync (which cancels the token) would never run. Run it on a
                        // background task and wait only until JSON-RPC is listening.
                        // Unlike xdebug_proxy, the LSP session ends when the client sends
                        // `exit` (or disconnects) — ContinueWith StopApplication so the host
                        // does not idle forever after the protocol shuts down.
                        // Stdout is reserved for LSP framing; banner / Ready / Stopping are
                        // suppressed via isMachineOutputAction and OnStarted/OnStopping.
                        var languageServerAction = new LanguageServerAction(this.project);
                        this._actionRunner = languageServerAction;
                        this._longRunningActionTask = Task.Run(
                            () => languageServerAction.Start(this._actionCancelTokenSource.Token));
                        this._isLongRunning = TryWaitForLanguageServerStartup(languageServerAction);
                        if (this._isLongRunning)
                        {
                            _ = this._longRunningActionTask.ContinueWith(
                                _ => this._appLifetime.StopApplication(),
                                CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
                        }
                        break;
                    case Tyhp.Config.Action.xdebug_proxy:
                        // XDebugProxyAction.Start() blocks on ProxyServer.StartAsync until
                        // cancelled. Calling it inline here (as earlier revisions did) would
                        // block this StartAsync method itself: the .NET Generic Host only
                        // invokes IHostedService.StopAsync (which is what cancels
                        // _actionCancelTokenSource) after every hosted service's StartAsync has
                        // returned. Ctrl+C would fire ApplicationStopping (so "Stopping..."
                        // still prints), but the token this call blocks on would never actually
                        // be cancelled — a permanent deadlock verified by running the built CLI
                        // and observing it hang past its own "Ready!"/"Stopping..." messages.
                        // Run it on a background task instead so StartAsync can return once
                        // startup (bind-or-fail) completes; StopAsync cancels the token and
                        // awaits this task so shutdown still waits for a graceful stop.
                        var xdebugProxyAction = new XDebugProxyAction(this.project);
                        this._actionRunner = xdebugProxyAction;
                        this._longRunningActionTask = Task.Run(
                            () => xdebugProxyAction.Start(this._actionCancelTokenSource.Token));
                        // Only block on startup (bind-or-fail), never on the proxy's full
                        // lifetime. On success this stays running for Ctrl+C; on a validation
                        // or bind failure (already reported + exit code set inside RunAsync)
                        // _isLongRunning stays false so the host exits immediately instead of
                        // idling forever waiting for a Ctrl+C that would never arrive from a
                        // dead action.
                        this._isLongRunning = TryWaitForXDebugProxyStartup(xdebugProxyAction);
                        break;
                    case Tyhp.Config.Action.generate_tyhpdef:
                        // generate tyhpdef file(s) for composer package or PHP module
                        this._actionRunner = new GenerateTyhpdefAction();
                        var genResult = this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        if (genResult != null)
                        {
                            Environment.ExitCode = (int)genResult.GetExitCode();
                        }
                        break;

                    // used for internal debugging
                    case Tyhp.Config.Action.debug:
                        Message.Error(
                            "CLI_InternalDebug",
                            Message.Localize("CLI_DebugMode")
                        );
                        bool doBind = this._configuration["bind"] != null;
                        this._actionRunner = new DebugAction(doBind);
                        var debugResult = this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        if (debugResult != null)
                        {
                            Environment.ExitCode = (int)debugResult.GetExitCode();
                        }
                        break;

                    case Tyhp.Config.Action.integrity_check:
                        // run integrity checks on this Tyhp build
                        this._actionRunner = new IntegrityCheckAction(this.project);
                        this._actionRunner.Start(this._actionCancelTokenSource.Token);
                        break;

                    case Tyhp.Config.Action.clear_cache:
                        // delete the on-disk AST cache for every compiler build
                        var clearedPath = Tyhp.Domain.Services.AstCacheService.ClearAll();
                        if (!this.project.BeQuiet)
                        {
                            Message.Success("CLI_CacheCleared", clearedPath);
                        }

                        Environment.ExitCode = (int)Tyhp.Domain.Enums.ExitCode.Success;
                        break;
                }

                if (!this._isLongRunning) {
                    this._appLifetime.StopApplication();
                }
            } finally {
            // A long-running action (xdebug_proxy, language_server) keeps running on a background
            // task after this method returns, so the pid file must stay put until that task
            // actually finishes (cleaned up from StopAsync instead). Actions that already ran
            // to completion inline are cleaned up here as before.
                if (this._pidFilePath is not null && this._longRunningActionTask is null) {
                    TryDeletePidFile(this._pidFilePath);
                    this._pidFilePath = null;
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Blocks only until <see cref="LanguageServerAction.WhenListening"/> resolves (JSON-RPC
        /// listening or startup failure), never until the client sends <c>exit</c>.
        /// </summary>
        private static bool TryWaitForLanguageServerStartup(LanguageServerAction action)
        {
            try
            {
                action.WhenListening.GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }

        /// <summary>
        /// Blocks only until <see cref="XDebugProxyAction.WhenListening"/> resolves (bind
        /// success/failure), never until the proxy actually stops. Bounded by the same
        /// validation/bind logic <see cref="XDebugProxyAction.RunAsync"/> already runs before
        /// entering its long-running accept loops, so this cannot deadlock the way blocking on
        /// the full action would.
        /// </summary>
        private static bool TryWaitForXDebugProxyStartup(XDebugProxyAction action)
        {
            try
            {
                action.WhenListening.GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // XDebugProxyAction.RunAsync already reported the diagnostic and set
                // Environment.ExitCode for both validation failures and port-in-use.
                return false;
            }
        }

        /// <summary>
        /// Writes the opt-in pid file, tolerating a path the user cannot write to.
        /// </summary>
        /// <remarks>
        /// The pid file is bookkeeping for process managers; failing to create it must not abort
        /// the run with an unhandled exception (e.g. <c>tyhp version</c> from a read-only directory).
        /// </remarks>
        private static string? TryWritePidFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                File.WriteAllText(path, Environment.ProcessId.ToString());
                return path;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
            {
                return null;
            }
        }

        private static void TryDeletePidFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                // Leaving a stale pid file behind is preferable to failing the run on cleanup.
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            this._actionCancelTokenSource.Cancel();

            if (this._longRunningActionTask is not null)
            {
                try
                {
                    // cancellationToken here is the host's shutdown-timeout token; bound the
                    // wait by it instead of hanging past a shutdown that the host has already
                    // decided to give up on.
                    await this._longRunningActionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
                {
                }
            }

            if (this._pidFilePath is not null)
            {
                TryDeletePidFile(this._pidFilePath);
                this._pidFilePath = null;
            }
        }

        private void OnStarted()
        {
            // language_server owns stdout for JSON-RPC; never print Ready/Stopping there.
            if (this._isLongRunning && !this.project.BeQuiet && !this.IsLanguageServerAction()) {
                Message.Success("CLI_Ready");
            }
        }

        private void OnStopping()
        {
            if (this._isLongRunning && !this.project.BeQuiet && !this.IsLanguageServerAction()) {
                Message.Warn("CLI_Stopping");
            }
        }

        private bool IsLanguageServerAction()
            => this._configuration["*action"] == Tyhp.Config.Action.language_server.ToString();

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this._actionRunner?.Dispose();
                    this._actionCancelTokenSource.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~TyhpHostedService()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}