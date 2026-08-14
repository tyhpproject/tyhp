using Tyhp.CLI.IntegrityChecks;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;

namespace Tyhp.CLI
{
    /// <summary>
    /// Runs project integrity checks (configuration, tyhpdef, AST cache, environment).
    /// </summary>
    public class IntegrityCheckAction : ActionRunnerBase
    {
        private readonly Project _project;

        public IntegrityCheckAction(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            try
            {
                this.RunAsync(cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Message.Warn("CLI_IntegrityCheckCancelled");
                Environment.ExitCode = (int)ExitCode.GenericError;
            }

            return null;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // Under --quiet only failures (and why they failed) are reported.
            var quiet = this._project.BeQuiet;
            if (!quiet)
            {
                Message.Info("CLI_RunningIntegrityChecks");
            }

            IIntegrityCheck[] checks =
            [
                new ConfigurationCheck(this._project),
                new TyhpdefCheck(this._project),
                new CacheCheck(this._project),
                new EnvironmentCheck(this._project),
            ];

            var passed = 0;
            var failed = 0;
            var verbose = this._project.Verbose;

            foreach (var check in checks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!quiet)
                {
                    Message.Info("CLI_IntegrityCheckStarting", check.Name);
                }

                IntegrityCheckResult result;
                try
                {
                    result = await check.RunAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    result = IntegrityCheckResult.Fail(
                        Message.Localize("CLI_IntegrityCheckUnexpectedError", ex.Message));
                }

                if (result.Passed)
                {
                    passed++;
                    if (!quiet)
                    {
                        Message.Success(
                            "CLI_IntegrityCheckItemPassed",
                            check.Name,
                            result.Message ?? string.Empty);
                    }
                }
                else
                {
                    failed++;
                    if (result.IsWarning)
                    {
                        Message.Warn(
                            "CLI_IntegrityCheckItemWarning",
                            check.Name,
                            result.Message ?? string.Empty);
                    }
                    else
                    {
                        Message.Error(
                            "CLI_IntegrityCheckItemFailed",
                            check.Name,
                            result.Message ?? string.Empty);
                    }
                }

                foreach (var problem in result.Problems)
                {
                    Message.Display("CLI_IntegrityCheckProblem", problem);
                }

                if (verbose)
                {
                    foreach (var detail in result.Details)
                    {
                        Message.Display("CLI_IntegrityCheckDetail", detail);
                    }
                }
            }

            var total = checks.Length;
            if (!quiet)
            {
                Message.Info("CLI_IntegrityCheckSummary", passed, total);
            }

            if (failed > 0)
            {
                Message.Error("CLI_IntegrityChecksFailed", failed, total);
                Environment.ExitCode = (int)ExitCode.IntegrityCheckFailed;
                return;
            }

            if (!quiet)
            {
                Message.Success("CLI_IntegrityChecksPassed");
            }

            Environment.ExitCode = (int)ExitCode.Success;
        }
    }
}
