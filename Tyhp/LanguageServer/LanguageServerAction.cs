namespace Tyhp.LanguageServer
{
    using Tyhp.CLI;
    using Tyhp.Config;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Enums;
    using Tyhp.Domain.Exceptions;
    using Tyhp.LanguageServer.Configuration;

    /// <summary>
    /// CLI action that starts the Tyhp Language Server over stdin/stdout.
    /// </summary>
    public sealed class LanguageServerAction : ActionRunnerBase
    {
        private static readonly AsyncLocal<(Stream Input, Stream Output)?> OverrideStreamsLocal = new();

        private readonly Project _project;
        private readonly Stream? _input;
        private readonly Stream? _output;
        private readonly TaskCompletionSource _listeningTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Optional stdin/stdout replacement so hosted-service tests do not steal
        /// the test runner's console. Flows across <see cref="Task.Run(Action)"/> via
        /// <see cref="ExecutionContext"/>.
        /// </summary>
        internal static (Stream Input, Stream Output)? OverrideStreams
        {
            get => OverrideStreamsLocal.Value;
            set => OverrideStreamsLocal.Value = value;
        }

        public LanguageServerAction(Project project)
            : this(project, input: null, output: null)
        {
        }

        internal LanguageServerAction(Project project, Stream? input, Stream? output)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
            this._input = input;
            this._output = output;
        }

        /// <summary>Completes after JSON-RPC starts listening, or faults if startup fails.</summary>
        internal Task WhenListening => this._listeningTcs.Task;

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            try
            {
                this.RunAsync(cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            return null;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var tcp = this._project.GetConfigValue("tcp");
            var pipe = this._project.GetConfigValue("pipe");
            if (!string.IsNullOrWhiteSpace(tcp) || !string.IsNullOrWhiteSpace(pipe))
            {
                // PLACEHOLDER_STORY_30: tcp transport
                // PLACEHOLDER_STORY_30: named-pipe transport
                Console.Error.WriteLine(Message.Localize("CLI_LanguageServerTransportNotImplemented"));
                Environment.ExitCode = (int)ExitCode.GenericError;
                this._listeningTcs.TrySetException(
                    new InvalidOperationException(Message.Localize("CLI_LanguageServerTransportNotImplemented")));
                return;
            }

            var (input, output, isRealStdio) = this.ResolveStreams();
            var configuration = ServerConfiguration.FromProject(this._project);

            try
            {
                await TyhpLanguageServer.RunAsync(
                    input,
                    output,
                    configuration,
                    cancellationToken,
                    onListening: () => this._listeningTcs.TrySetResult(),
                    allowForcedProcessExit: isRealStdio).ConfigureAwait(false);

                if (Environment.ExitCode == 0)
                {
                    Environment.ExitCode = (int)ExitCode.Success;
                }
            }
            catch (OperationCanceledException)
            {
                this._listeningTcs.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine(
                    Message.LocalizeErrorCode((int)MessageCode.LspServerStartupFailed, ex.Message));
                Environment.ExitCode = (int)ExitCode.GenericError;
                this._listeningTcs.TrySetException(ex);
            }
        }

        /// <summary>
        /// Resolves the transport streams plus whether they are the real
        /// <c>Console.OpenStandardInput()</c>/<c>Console.OpenStandardOutput()</c> pair, which
        /// <see cref="TyhpLanguageServer"/> uses to decide whether <c>exit</c> may fall back to
        /// <see cref="Environment.Exit(int)"/> if disposal alone does not unblock a pending read
        /// (see <see cref="TyhpLanguageServer.Exit"/>). Explicit constructor streams and
        /// <see cref="OverrideStreams"/> are always test-owned, cancellable streams, so forcing
        /// a process exit on them is never appropriate.
        /// </summary>
        private (Stream Input, Stream Output, bool IsRealStdio) ResolveStreams()
        {
            if (this._input is not null && this._output is not null)
            {
                return (this._input, this._output, false);
            }

            if (OverrideStreams is { } overrideStreams)
            {
                return (overrideStreams.Input, overrideStreams.Output, false);
            }

            return (Console.OpenStandardInput(), Console.OpenStandardOutput(), true);
        }
    }
}
