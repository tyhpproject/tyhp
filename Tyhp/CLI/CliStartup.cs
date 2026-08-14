using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Tyhp.Domain.Enums;

namespace Tyhp.CLI
{
    /// <summary>
    /// Early-startup helpers used before the host (and its DI localizer) are available.
    /// Catches configuration / argv failures that would otherwise escape as exit 134.
    /// </summary>
    internal static class CliStartup
    {
        private static ServiceProvider? _bootstrapProvider;

        /// <summary>
        /// Ensures <see cref="Message"/> can resolve <c>CLI_*</c> keys before
        /// <see cref="TyhpHostedService"/> constructs the real localizer.
        /// </summary>
        /// <remarks>
        /// Failing to build the bootstrap localizer must not become the crash this class exists to
        /// prevent: <see cref="Message"/> falls back to the embedded <c>CLI.TyhpHostedService</c>
        /// resources on its own, so the error still prints in English.
        /// </remarks>
        public static void EnsureLocalizer()
        {
            if (_bootstrapProvider != null)
            {
                return;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options =>
            {
                options.ResourcesPath = "Resources";
            });
            var provider = services.BuildServiceProvider();
            try
            {
                var factory = provider.GetRequiredService<IStringLocalizerFactory>();
                Message.SetLocalizer(factory.Create(typeof(TyhpHostedService)));
                _bootstrapProvider = provider;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                provider.Dispose();
            }
        }

        /// <summary>
        /// Rejects single-dash flags that carry an inline value (<c>-d=x</c>). Those forms throw
        /// from .NET's <c>CommandLineConfigurationProvider</c> when no switch mappings are registered.
        /// </summary>
        /// <returns><see langword="true"/> when argv is safe to bind; otherwise reports and sets exit code.</returns>
        public static bool TryValidateArgs(string[] args)
        {
            foreach (var arg in args)
            {
                if (!IsShortSwitchWithValue(arg))
                {
                    continue;
                }

                EnsureLocalizer();
                Message.Error("CLI_StartupInvalidShortSwitch", arg);
                Environment.ExitCode = (int)ExitCode.GenericError;
                return false;
            }

            return true;
        }

        /// <summary>
        /// When <c>--tyhp-project</c> is explicit, require the target file to exist before the host
        /// builds a non-optional JSON configuration source.
        /// </summary>
        public static bool TryValidateProjectFile(string[] args)
        {
            if (!TryGetTyhpProjectPath(args, out var projectPath) || projectPath == null)
            {
                return true;
            }

            if (File.Exists(projectPath))
            {
                return true;
            }

            EnsureLocalizer();
            Message.Error("CLI_StartupProjectFileNotFound", projectPath);
            Environment.ExitCode = (int)ExitCode.GenericError;
            return false;
        }

        /// <summary>
        /// Reports a configuration / argv failure that escaped from host build or run.
        /// </summary>
        public static void ReportConfigurationFailure(Exception exception)
        {
            EnsureLocalizer();

            switch (exception)
            {
                case FileNotFoundException fnf:
                    Message.Error(
                        "CLI_StartupProjectFileNotFound",
                        fnf.FileName ?? fnf.Message);
                    break;
                case FormatException format when LooksLikeShortSwitchError(format.Message):
                    Message.Error("CLI_StartupInvalidShortSwitch", ExtractShortSwitch(format.Message) ?? format.Message);
                    break;
                case FormatException format:
                    Message.Error("CLI_StartupConfigInvalid", format.Message);
                    break;
                case InvalidDataException invalid:
                    // The outer message only names the file; the parser's inner message is what
                    // tells the user which line to fix.
                    Message.Error("CLI_StartupConfigInvalid", DescribeWithCause(invalid));
                    break;
                case System.Text.Json.JsonException json:
                    Message.Error("CLI_StartupConfigInvalid", json.Message);
                    break;
                default:
                    Message.Error("CLI_StartupConfigInvalid", exception.Message);
                    break;
            }

            Environment.ExitCode = (int)ExitCode.GenericError;
        }

        /// <summary>
        /// True for exceptions that should become a friendly CLI error instead of an unhandled crash.
        /// </summary>
        public static bool IsConfigurationFailure(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is FormatException
                    or FileNotFoundException
                    or InvalidDataException
                    or System.Text.Json.JsonException)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Unwraps <see cref="AggregateException"/> / nested inners to the configuration failure
        /// that should be reported to the user.
        /// </summary>
        public static Exception UnwrapConfigurationFailure(Exception exception)
        {
            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    if (IsConfigurationFailure(inner))
                    {
                        return UnwrapConfigurationFailure(inner);
                    }
                }
            }

            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is FormatException
                    or FileNotFoundException
                    or InvalidDataException
                    or System.Text.Json.JsonException)
                {
                    return current;
                }
            }

            return exception;
        }

        private static string DescribeWithCause(Exception exception)
        {
            // The configuration stack wraps twice (file → "could not parse" → parser); the innermost
            // message is the one carrying the line and position the user has to fix.
            Exception root = exception;
            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            return ReferenceEquals(root, exception) || String.IsNullOrWhiteSpace(root.Message)
                ? exception.Message
                : exception.Message + " " + root.Message;
        }

        private static bool IsShortSwitchWithValue(string arg)
        {
            // `-d=x` / `-q=true` — single dash, not `--`, and an inline value.
            if (arg.Length < 3 || arg[0] != '-' || arg[1] == '-')
            {
                return false;
            }

            return arg.Contains('=', StringComparison.Ordinal);
        }

        private static bool LooksLikeShortSwitchError(string message)
            => message.Contains("short switch", StringComparison.OrdinalIgnoreCase);

        private static string? ExtractShortSwitch(string message)
        {
            // "The short switch '-d=x' is not defined in the switch mappings."
            var start = message.IndexOf('\'');
            var end = start >= 0 ? message.IndexOf('\'', start + 1) : -1;
            if (start >= 0 && end > start)
            {
                return message[(start + 1)..end];
            }

            return null;
        }

        private static bool TryGetTyhpProjectPath(string[] args, out string? path)
        {
            path = null;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--tyhp-project=", StringComparison.OrdinalIgnoreCase))
                {
                    path = arg["--tyhp-project=".Length..];
                    return !string.IsNullOrWhiteSpace(path);
                }

                if (string.Equals(arg, "--tyhp-project", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        return false;
                    }

                    path = args[i + 1];
                    return !string.IsNullOrWhiteSpace(path);
                }
            }

            return false;
        }
    }
}
