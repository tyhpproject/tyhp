using System.Diagnostics;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Services;
using Tyhp.Extensions;

namespace Tyhp.CLI
{
    /// <summary>
    /// Proxies Composer CLI commands with optional Tyhp tyhpdef post-hooks
    /// (<c>tyhp composer &lt;command&gt; [args]</c>).
    /// </summary>
    public class ComposerAction : ActionRunnerBase
    {
        /// <summary>
        /// Tyhp-owned boolean flags. A following <c>true</c>/<c>false</c> literal belongs to the flag;
        /// anything else is a Composer token.
        /// </summary>
        private static readonly HashSet<string> TyhpOwnedBooleanFlagNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "no-tyhpdef",
                "quiet",
                "json",
                "help",
            };

        /// <summary>
        /// Tyhp-owned flags that always carry a value, whether inline (<c>--locale=en-US</c>) or as the
        /// next token (<c>--locale en-US</c>).
        /// </summary>
        private static readonly HashSet<string> TyhpOwnedValueFlagNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "tyhp-project",
                "locale",
                "pid-file",
            };

        private static readonly HashSet<string> TyhpdefTriggerCommands =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "require",
                "install",
                "update",
            };

        internal Func<string?>? FindPhp { get; init; }

        internal Func<string, string?>? ResolveComposer { get; init; }

        internal Func<string, IReadOnlyList<string>, string, int>? RunComposerProcess { get; init; }

        /// <summary>Post-action argv. Tests inject this; production reads <see cref="ActionConfigProvider.RemainingArgs"/>.</summary>
        internal IReadOnlyList<string>? Args { get; init; }

        internal VendorTyhpdefGenerator? VendorGenerator { get; init; }

        internal Func<string, string?>? ReadEnvironment { get; init; }

        internal TyhpdefGenerationResult? LastVendorResult { get; private set; }

        private readonly Project _project;

        public ComposerAction(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var composerArgs = FilterComposerArgs(this.Args ?? ActionConfigProvider.RemainingArgs);
            if (composerArgs.Count == 0)
            {
                // Prefer Tyhp's composer help over forwarding a bare `composer` with no subcommand.
                DisplayHelp.ComposerHelp();
                Environment.ExitCode = (int)ExitCode.Success;
                return null;
            }

            if (IsSyncCommand(composerArgs))
            {
                return this.RunExtrasSync(cancellationToken);
            }

            if (!this.TryFindPhp(out _))
            {
                Message.Error("CLI_ComposerPhpNotFound");
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            if (!this.TryResolveComposer(out var composerExecutable))
            {
                Message.Error("CLI_ComposerActionNotFound");
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Composer 2.2+ refuses tyhp/core until allow-plugins is set; the plugin cannot
            // write that key itself. Patch before any Composer invocation that would load it.
            InitComposerManifest.TryEnsureAllowPlugins(
                Path.Combine(this._project.GetProjectPath(), "composer.json"),
                out _);

            var exitCode = this.InvokeComposer(composerExecutable, composerArgs, this._project.GetProjectPath());
            Environment.ExitCode = exitCode;

            if (exitCode == 0
                && ShouldOfferTyhpdefHook(composerArgs)
                && !this._project.GetConfigValue("no-tyhpdef").ParseBool())
            {
                this.RunVendorTyhpdefHook(cancellationToken);
            }

            return null;
        }

        /// <summary>
        /// Drops Tyhp-owned flags from the post-action argv so only Composer-bound tokens remain.
        /// </summary>
        internal static IReadOnlyList<string> FilterComposerArgs(IReadOnlyList<string> remainingArgs)
        {
            var filtered = new List<string>(remainingArgs.Count);
            for (var i = 0; i < remainingArgs.Count; i++)
            {
                var arg = remainingArgs[i];
                if (!TryGetLongFlagName(arg, out var flagName))
                {
                    filtered.Add(arg);
                    continue;
                }

                var hasInlineValue = arg.Contains('=', StringComparison.Ordinal);
                var nextArg = i + 1 < remainingArgs.Count ? remainingArgs[i + 1] : null;

                if (TyhpOwnedValueFlagNames.Contains(flagName))
                {
                    // Space-separated value forms (`--tyhp-project ./tyhp.json`) must not leak the
                    // value token into the Composer argv.
                    if (!hasInlineValue && nextArg != null && !nextArg.StartsWith('-'))
                    {
                        i++;
                    }

                    continue;
                }

                if (TyhpOwnedBooleanFlagNames.Contains(flagName))
                {
                    // Consume only an explicit boolean literal. A Composer subcommand that happens to
                    // follow the flag (`--no-tyhpdef require foo/bar`) is not its value.
                    if (!hasInlineValue && nextArg != null && IsBooleanLiteral(nextArg))
                    {
                        i++;
                    }

                    continue;
                }

                filtered.Add(arg);
            }

            return filtered;
        }

        /// <summary>
        /// Reduces the post-action argv to the Tyhp-owned flags before it reaches the configuration
        /// binder. Non-proxy actions get their argv back unchanged.
        /// </summary>
        /// <remarks>
        /// .NET's <c>CommandLineConfigurationProvider</c> treats any <c>--flag</c> without an <c>=</c> as
        /// a key whose value is the next token, and Composer's flag set is open-ended, so leaving
        /// Composer's tokens in place lets one of them swallow a Tyhp flag
        /// (<c>composer install --no-interaction --no-tyhpdef</c> would drop <c>--no-tyhpdef</c>). Values
        /// are normalized to the inline <c>--flag=value</c> spelling so nothing can consume a neighbor.
        /// </remarks>
        internal static string[] SelectTyhpConfigArgs(Tyhp.Config.Action action, string[] postActionArgs)
        {
            if (action != Tyhp.Config.Action.composer)
            {
                return postActionArgs;
            }

            var selected = new List<string>(postActionArgs.Length);
            for (var i = 0; i < postActionArgs.Length; i++)
            {
                var arg = postActionArgs[i];
                if (!TryGetLongFlagName(arg, out var flagName))
                {
                    continue;
                }

                var isValueFlag = TyhpOwnedValueFlagNames.Contains(flagName);
                if (!isValueFlag && !TyhpOwnedBooleanFlagNames.Contains(flagName))
                {
                    continue;
                }

                if (arg.Contains('=', StringComparison.Ordinal))
                {
                    selected.Add(arg);
                    continue;
                }

                var nextArg = i + 1 < postActionArgs.Length ? postActionArgs[i + 1] : null;

                if (isValueFlag)
                {
                    if (nextArg != null && !nextArg.StartsWith('-'))
                    {
                        selected.Add(arg + "=" + nextArg);
                        i++;
                    }

                    continue;
                }

                if (nextArg != null && IsBooleanLiteral(nextArg))
                {
                    selected.Add(arg + "=" + nextArg);
                    i++;
                    continue;
                }

                selected.Add(arg + "=true");
            }

            return selected.ToArray();
        }

        internal static bool ShouldOfferTyhpdefHook(IReadOnlyList<string> composerArgs)
        {
            foreach (var arg in composerArgs)
            {
                if (arg.StartsWith('-'))
                {
                    continue;
                }

                return TyhpdefTriggerCommands.Contains(arg);
            }

            return false;
        }

        /// <summary>
        /// True when the first Composer token is Tyhp's <c>sync</c> subcommand (not proxied).
        /// </summary>
        internal static bool IsSyncCommand(IReadOnlyList<string> composerArgs)
        {
            foreach (var arg in composerArgs)
            {
                if (arg.StartsWith('-'))
                {
                    continue;
                }

                return string.Equals(arg, "sync", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private CompilationResult? RunExtrasSync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            InitComposerManifest.TryEnsureAllowPlugins(
                Path.Combine(this._project.GetProjectPath(), "composer.json"),
                out _);

            var result = new CompilationResult(this._project.SuppressedWarnings);
            var check = new ComposerExtraRequireCheck
            {
                RunComposerProcess = this.RunComposerProcess,
                ResolveComposer = this.ResolveComposer,
            };

            var ok = check.TryApply(
                this._project,
                result.Diagnostics,
                fix: true,
                dryRun: false);

            if (result.Diagnostics.All.Count > 0)
            {
                result.Diagnostics.DisplayAll(new ConsoleDiagnosticFormatter(this._project.BeQuiet));
            }

            Environment.ExitCode = (int)result.GetExitCode(this._project.Strict);
            if (!ok && Environment.ExitCode == (int)ExitCode.Success)
            {
                Environment.ExitCode = (int)ExitCode.GenericError;
            }

            if (ok
                && Environment.ExitCode == (int)ExitCode.Success
                && !this._project.GetConfigValue("no-tyhpdef").ParseBool())
            {
                this.RunVendorTyhpdefHook(cancellationToken);
            }

            return null;
        }

        private bool TryFindPhp(out string phpPath)
        {
            if (this.FindPhp is not null)
            {
                phpPath = this.FindPhp() ?? "";
                return !string.IsNullOrWhiteSpace(phpPath);
            }

            return ExternalToolLocator.TryFindExecutable("php", out phpPath)
                && ExternalToolLocator.TryProbeVersion(phpPath);
        }

        private bool TryResolveComposer(out string composerExecutable)
        {
            if (this.ResolveComposer is not null)
            {
                composerExecutable = this.ResolveComposer(this._project.GetProjectPath()) ?? "";
                return !string.IsNullOrWhiteSpace(composerExecutable);
            }

            return ExternalToolLocator.TryResolveComposerExecutable(
                this._project.GetProjectPath(),
                out composerExecutable);
        }

        private int InvokeComposer(
            string composerExecutable,
            IReadOnlyList<string> composerArgs,
            string workingDirectory)
        {
            if (this.RunComposerProcess is not null)
            {
                return this.RunComposerProcess(composerExecutable, composerArgs, workingDirectory);
            }

            return RunComposer(composerExecutable, composerArgs, workingDirectory);
        }

        private void RunVendorTyhpdefHook(CancellationToken cancellationToken)
        {
            var env = this.ReadEnvironment ?? Environment.GetEnvironmentVariable;
            if (VendorTyhpdefLayout.IsEnvironmentFlagSet(
                    env(VendorTyhpdefLayout.VendorRunningEnvironmentVariable))
                || VendorTyhpdefLayout.IsEnvironmentFlagSet(
                    env(VendorTyhpdefLayout.NoVendorEnvironmentVariable)))
            {
                return;
            }

            var generator = this.VendorGenerator ?? new VendorTyhpdefGenerator();
            var vendorDir = this._project.GetTyhpdefVendorDirectory()
                ?? Path.GetFullPath(Path.Combine(this._project.GetProjectPath(), "vendor"));
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.Vendor,
                OutputDirectory = VendorTyhpdefLayout.OutputDirectory(this._project.GetProjectPath()),
                Overwrite = this._project.GetTyhpdefOverwrite(),
                IncludeDocComments = !this._project.GetTyhpdefNoDocs(),
                IncludeDeprecated = this._project.GetTyhpdefIncludeDeprecated(),
                IncludeInternal = this._project.GetTyhpdefIncludeInternal(),
                IncludeDev = this._project.GetTyhpdefIncludeDev(),
                PhpExecutablePath = this._project.GetPhpExecutablePath(),
                PhpRuntimeDir = this._project.GetTyhpdefPhpRuntimeDir(),
                NoPhpRuntimeUpdate = this._project.GetTyhpdefNoPhpRuntimeUpdate(),
                PhpVersion = this._project.GetTyhpdefPhpVersion() ?? this._project.PhpVersion,
                SnapshotDirectory = Path.Combine(this._project.GetProjectPath(), "tyhpdef_gen", "snapshots"),
                FetchStubCache = true,
            };

            var result = new TyhpdefGenerationResult();
            generator.Generate(this._project, vendorDir, options, result, cancellationToken);
            this.LastVendorResult = result;
            if (!result.Success)
            {
                Environment.ExitCode = (int)ExitCode.GenericError;
            }
        }

        private static bool IsBooleanLiteral(string token)
            => string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "false", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetLongFlagName(string arg, out string flagName)
        {
            flagName = string.Empty;
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            var body = arg.AsSpan(2);
            var eq = body.IndexOf('=');
            flagName = (eq >= 0 ? body[..eq] : body).ToString();
            return flagName.Length > 0;
        }

        private static int RunComposer(
            string composerExecutable,
            IReadOnlyList<string> composerArgs,
            string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                // Inherit the parent console so Composer output streams in real time and large
                // installs cannot deadlock on a full redirected pipe buffer.
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (composerExecutable.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "php";
                startInfo.ArgumentList.Add(composerExecutable);
            }
            else
            {
                startInfo.FileName = composerExecutable;
            }

            foreach (var arg in composerArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    Message.Error("CLI_ComposerActionNotFound");
                    return (int)ExitCode.GenericError;
                }

                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Message.Error("CLI_ComposerProxyFailed", ex.Message);
                return (int)ExitCode.GenericError;
            }
        }
    }
}
