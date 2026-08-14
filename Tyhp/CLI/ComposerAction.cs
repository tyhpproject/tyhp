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
            };

        private static readonly HashSet<string> TyhpdefTriggerCommands =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "require",
                "install",
                "update",
            };

        private readonly Project _project;

        public ComposerAction(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var composerArgs = FilterComposerArgs(ActionConfigProvider.RemainingArgs);
            if (composerArgs.Count == 0)
            {
                // Prefer Tyhp's composer help over forwarding a bare `composer` with no subcommand.
                DisplayHelp.ComposerHelp();
                Environment.ExitCode = (int)ExitCode.Success;
                return null;
            }

            if (!ExternalToolLocator.TryFindExecutable("php", out var phpPath)
                || !ExternalToolLocator.TryProbeVersion(phpPath))
            {
                Message.Error("CLI_ComposerPhpNotFound");
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            if (!ExternalToolLocator.TryResolveComposerExecutable(
                    this._project.GetProjectPath(),
                    out var composerExecutable))
            {
                Message.Error("CLI_ComposerActionNotFound");
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var exitCode = RunComposer(composerExecutable, composerArgs, this._project.GetProjectPath());
            Environment.ExitCode = exitCode;

            if (exitCode == 0
                && ShouldOfferTyhpdefHook(composerArgs)
                && !this._project.GetConfigValue("no-tyhpdef").ParseBool())
            {
                // PLACEHOLDER_STORY_20: auto-generate tyhpdef after composer install/update
                if (!this._project.BeQuiet)
                {
                    Message.Info("CLI_ComposerTyhpdefDeferred");
                }
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
