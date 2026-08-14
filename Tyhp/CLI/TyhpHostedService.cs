namespace Tyhp.CLI
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Primitives;

    class TyhpHostedService : IHostedService, IDisposable
    {
        private const string PidFileName = "tyhp.pid";

        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IConfiguration _configuration;
        private readonly Tyhp.Config.Project project;
        private readonly IStringLocalizer<TyhpHostedService>? _localizer = null;
        private bool _isLongRunning = false;
        private CancellationTokenSource _actionCancelTokenSource;
        private ActionRunnerBase? _actionRunner;
        private bool disposedValue;

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
                || (rawAction == Tyhp.Config.Action.lint.ToString()
                    && LintAction.UsesMachineReadableOutput(this.project.LintFormat))
                || versionJson;

            // display the cli banner (suppressed for --quiet and machine-readable formats)
            if (!this.project.BeQuiet && !isMachineOutputAction) {
                Message.Banner();
            }

            // write Environment.ProcessId to a file
            // TODO: make this configurable to not wrote or to write to a different file
            // TODO: this is especially useful is specified from a CLI argument
            var pidFileWritten = TryWritePidFile();
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
                        // LanguageServerAction lands with Story 19; do not set _isLongRunning here —
                        // that would leave the host running with no work to cancel.
                        Message.Error("CLI_LanguageServerNotImplemented");
                        Environment.ExitCode = (int)Tyhp.Domain.Enums.ExitCode.GenericError;
                        break;
                    case Tyhp.Config.Action.xdebug_proxy:
                        // PLACEHOLDER_STORY_18: XDebugProxyAction
                        Message.Error("CLI_XDebugProxyNotImplemented");
                        Environment.ExitCode = (int)Tyhp.Domain.Enums.ExitCode.GenericError;
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
                // TODO: use configuration to get file name
                if (pidFileWritten) {
                    TryDeletePidFile();
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Writes the pid file, tolerating a working directory the user cannot write to.
        /// </summary>
        /// <remarks>
        /// The pid file is bookkeeping for long-running actions; failing to create it must not abort
        /// the run with an unhandled exception (e.g. <c>tyhp version</c> from a read-only directory).
        /// </remarks>
        private static bool TryWritePidFile()
        {
            try
            {
                File.WriteAllText(PidFileName, Environment.ProcessId.ToString());
                return true;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                return false;
            }
        }

        private static void TryDeletePidFile()
        {
            try
            {
                File.Delete(PidFileName);
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
            // TODO: stop running stuff (language server, xdebug proxy, build watch, etc.)
            this._actionCancelTokenSource.Cancel();
            await Task.CompletedTask;
        }

        private void OnStarted()
        {
            if (this._isLongRunning && !this.project.BeQuiet) {
                Message.Success("CLI_Ready");
            }
        }

        private void OnStopping()
        {
            if (this._isLongRunning && !this.project.BeQuiet) {
                Message.Warn("CLI_Stopping");
            }
        }

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