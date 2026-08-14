using System.Reflection;
using System.Resources;
using Konsole;
using Microsoft.Extensions.Localization;

namespace Tyhp.CLI
{
    static class Message
    {
        private static readonly ConcurrentWriter ConcurrentWriter = new();

        /// <summary>
        /// Culture-neutral CLI strings from <c>Resources/CLI.TyhpHostedService.resx</c>. Used when
        /// <see cref="SetLocalizer"/> has not run yet (e.g. host build failures in <c>Program.cs</c>).
        /// </summary>
        private static readonly Lazy<ResourceManager?> FallbackResources = new(() =>
        {
            try
            {
                return new ResourceManager(
                    "tyhp.Resources.CLI.TyhpHostedService",
                    typeof(Message).Assembly);
            }
            catch (Exception ex) when (ex is MissingManifestResourceException or ArgumentException)
            {
                return null;
            }
        });

        internal class VersionHelper
        {
            public string GetAssemblyVersion()
            {
                var assembly = this.GetType().Assembly;
                var informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    var plus = informational.IndexOf('+');
                    return plus >= 0 ? informational[..plus] : informational;
                }

                return assembly.GetName().Version?.ToString() ?? "";
            }
        }

        private static IStringLocalizer? _localizer;
        private static int _localizerVersion;

        /// <summary>
        /// Bumped every time the localizer changes. Callers that cache resolved text can compare it
        /// against the version they cached under to detect that their copy is stale.
        /// </summary>
        public static int LocalizerVersion => Message._localizerVersion;

        /// <summary>
        /// Sets the string localizer instance used for message localization.
        /// </summary>
        /// <param name="localizer">The IStringLocalizer instance to use for localization</param>
        /// <remarks>
        /// This method should be called during application startup to enable localization.
        /// Must be called before any localization methods are invoked.
        /// </remarks>
        public static void SetLocalizer(IStringLocalizer localizer)
        {
            Message._localizer = localizer;
            Message._localizerVersion++;
        }

        /// <summary>
        /// Drops the localizer set by <see cref="SetLocalizer"/>, returning message lookup to the
        /// embedded <c>CLI.TyhpHostedService</c> resources.
        /// </summary>
        /// <remarks>
        /// <see cref="SetLocalizer"/> writes process-wide state. Tests that install a stub localizer
        /// must reset it afterwards, or every later test in the run reads the stub's text instead of
        /// the real catalog.
        /// </remarks>
        public static void ResetLocalizer()
        {
            Message._localizer = null;
            Message._localizerVersion++;
        }

        /// <summary>
        /// Localizes a message code to its human-readable message.
        /// </summary>
        /// <param name="prefix">The message code prefix (e.g., "ERROR_TYHP", "WARNING_TYHP", "INFO_TYHP")</param>
        /// <param name="code">The MessageCode numeric value</param>
        /// <param name="args">Optional format arguments for the message template</param>
        /// <returns>The localized message with format parameters applied</returns>
        /// <remarks>
        /// Looks up the resource key "{prefix}{code}" in the resource file.
        /// If the localizer is not set or the key is not found, returns the raw key.
        /// </remarks>
        private static string LocalizeMessageCode(string prefix, int code, params object[]? args)
        {
            return Message.LocalizeStringFormat(prefix + code.ToString(), args);
        }

        /// <summary>
        /// Localizes an error code to its human-readable message.
        /// </summary>
        /// <param name="code">The MessageCode numeric value (e.g., 1001 for ParserUnknownError)</param>
        /// <param name="args">Optional format arguments for the error message template</param>
        /// <returns>The localized error message with format parameters applied</returns>
        /// <remarks>
        /// Looks up the resource key "ERROR_TYHP{code}" in the resource file.
        /// If the localizer is not set or the key is not found, returns the raw key.
        /// </remarks>
        public static string LocalizeErrorCode(int code, params object[]? args)
        {
            return Message.LocalizeMessageCode("ERROR_TYHP", code, args);
        }

        /// <summary>
        /// Localizes a warning code to its human-readable message.
        /// </summary>
        /// <param name="code">The MessageCode numeric value</param>
        /// <param name="args">Optional format arguments for the warning message template</param>
        /// <returns>The localized warning message with format parameters applied</returns>
        /// <remarks>
        /// Looks up the resource key "WARNING_TYHP{code}" in the resource file.
        /// If the localizer is not set or the key is not found, returns the raw key.
        /// </remarks>
        public static string LocalizeWarningCode(int code, params object[]? args)
        {
            return Message.LocalizeMessageCode("WARNING_TYHP", code, args);
        }

        /// <summary>
        /// Localizes an info code to its human-readable message.
        /// </summary>
        /// <param name="code">The MessageCode numeric value</param>
        /// <param name="args">Optional format arguments for the info message template</param>
        /// <returns>The localized info message with format parameters applied</returns>
        /// <remarks>
        /// Looks up the resource key "INFO_TYHP{code}" in the resource file.
        /// If the localizer is not set or the key is not found, returns the raw key.
        /// </remarks>
        public static string LocalizeInfoCode(int code, params object[]? args)
        {
            return Message.LocalizeMessageCode("INFO_TYHP", code, args);
        }

        /// <summary>
        /// Looks up and formats a localized string by resource key.
        /// </summary>
        /// <param name="msg">The resource key to look up</param>
        /// <param name="args">Optional format arguments for the localized string</param>
        /// <returns>The localized and formatted string</returns>
        /// <remarks>
        /// Uses the IStringLocalizer from <see cref="SetLocalizer"/> when available; otherwise falls
        /// back to the embedded <c>CLI.TyhpHostedService</c> resources. Both .resx files must stay
        /// in sync. If the key is not found, returns the raw key.
        /// </remarks>
        public static string Localize(string msg, params object[]? args)
        {
            msg = Message.ResolveResourceString(msg);
            return String.Format(msg, args ?? Array.Empty<string>());
        }

        /// <summary>
        /// Looks up a localized string by resource key without applying format arguments.
        /// </summary>
        /// <param name="key">The resource key to look up (e.g. <c>ERROR_TYHP4002</c>).</param>
        /// <returns>The raw localized template, or <paramref name="key"/> if unresolved.</returns>
        /// <remarks>
        /// Used by machine-readable formatters (e.g. SARIF rule short descriptions) that need the
        /// template text with <c>{0}</c>/<c>{1}</c> placeholders still present so they can strip them.
        /// </remarks>
        public static string LocalizeRaw(string key)
        {
            return Message.ResolveResourceString(key);
        }

        private static string ResolveResourceString(string key)
        {
            if (Message._localizer != null)
            {
                return Message._localizer[key];
            }

            try
            {
                var fallback = Message.FallbackResources.Value?.GetString(key);
                if (!String.IsNullOrEmpty(fallback))
                {
                    return fallback;
                }
            }
            catch (MissingManifestResourceException)
            {
                // Fall through to the raw key.
            }

            return key;
        }

        private static string LocalizeStringFormat(string msg, object[]? args)
            => Message.Localize(msg, args);

        public static void Banner()
        {
            var version = (new Message.VersionHelper()).GetAssemblyVersion();
            ConcurrentWriter.WriteLine(Message.Localize("CLI_Banner", version));
            ConcurrentWriter.WriteLine("");
        }

        public static void TyhpError(string msg, params object[]? args)
        {
            var currentBackgroundColor = ConcurrentWriter.BackgroundColor;
            ConcurrentWriter.BackgroundColor = ConsoleColor.Red;
            ConcurrentWriter.WriteLine(
                currentBackgroundColor,
                Message.LocalizeStringFormat(msg, args)
            );
            ConcurrentWriter.BackgroundColor = currentBackgroundColor;
        }

        public static void TyhpWarn(string msg, params object[]? args)
        {
            var currentBackgroundColor = ConcurrentWriter.BackgroundColor;
            ConcurrentWriter.BackgroundColor = ConsoleColor.Yellow;
            ConcurrentWriter.WriteLine(
                currentBackgroundColor,
                Message.LocalizeStringFormat(msg, args)
            );
            ConcurrentWriter.BackgroundColor = currentBackgroundColor;
        }

        public static void TyhpInfo(string msg, params object[]? args)
        {
            var currentBackgroundColor = ConcurrentWriter.BackgroundColor;
            ConcurrentWriter.BackgroundColor = ConsoleColor.Blue;
            ConcurrentWriter.WriteLine(
                currentBackgroundColor,
                Message.LocalizeStringFormat(msg, args)
            );
            ConcurrentWriter.BackgroundColor = currentBackgroundColor;
        }

        /// <summary>
        /// Writes a single compiler diagnostic line with segmented coloring:
        /// the "filename(line,column): severity " prefix and the ": " separator use the
        /// default console colors, the "TYHP####" code keeps the severity's highlighted
        /// background, and the message text is rendered in blue. This makes the code and
        /// message easier to scan than a fully highlighted line.
        /// </summary>
        private static void TyhpDiagnosticLine(
            ConsoleColor codeBackgroundColor,
            string fileName,
            int lineNumber,
            int column,
            string severityLabel,
            int code,
            string message)
        {
            var defaultBackgroundColor = ConcurrentWriter.BackgroundColor;

            // Prefix ("filename(line,column): severity ") in the default colors.
            ConcurrentWriter.Write(
                fileName + "(" + lineNumber + "," + column + "): " + severityLabel + " "
            );

            // "TYHP####" keeps the highlighted background (matching the previous styling).
            ConcurrentWriter.BackgroundColor = codeBackgroundColor;
            ConcurrentWriter.Write(defaultBackgroundColor, "TYHP" + code.ToString());
            ConcurrentWriter.BackgroundColor = defaultBackgroundColor;

            // Separator in the default colors.
            ConcurrentWriter.Write(": ");

            // Message text in blue for readability.
            ConcurrentWriter.WriteLine(ConsoleColor.Blue, message);
        }

        public static void TyhpError(string fileName, int lineNumber, int column, int code, params object[]? args)
        {
            Message.TyhpDiagnosticLine(
                ConsoleColor.Red,
                fileName,
                lineNumber,
                column,
                Message.LocalizeStringFormat("error", null),
                code,
                Message.LocalizeErrorCode(code, args)
            );
        }

        public static void TyhpWarn(string fileName, int lineNumber, int column, int code, params object[]? args)
        {
            Message.TyhpDiagnosticLine(
                ConsoleColor.Yellow,
                fileName,
                lineNumber,
                column,
                Message.LocalizeStringFormat("warning", null),
                code,
                Message.LocalizeWarningCode(code, args)
            );
        }

        /// <summary>
        /// Writes a warning diagnostic line to stderr (no Konsole / stdout).
        /// Use for early/status warnings when stdout must stay a machine-readable document
        /// (e.g. <c>version --json</c>, <c>tokenize</c>, <c>dump-ast</c>).
        /// </summary>
        public static void TyhpWarnToStderr(
            string fileName,
            int lineNumber,
            int column,
            int code,
            params object[]? args)
        {
            var severity = Message.LocalizeStringFormat("warning", null);
            var message = Message.LocalizeWarningCode(code, args);
            Console.Error.WriteLine(
                fileName + "(" + lineNumber + "," + column + "): "
                + severity + " TYHP" + code.ToString() + ": " + message);
        }

        public static void TyhpInfo(string fileName, int lineNumber, int column, int code, params object[]? args)
        {
            Message.TyhpDiagnosticLine(
                ConsoleColor.Blue,
                fileName,
                lineNumber,
                column,
                Message.LocalizeStringFormat("info", null),
                code,
                Message.LocalizeInfoCode(code, args)
            );
        }

        /// <summary>
        /// Writes a pre-formatted diagnostic source / gutter line in the default console colors.
        /// Used by <see cref="RichDiagnosticRenderer"/> for rustc-style snippets.
        /// </summary>
        public static void DiagnosticSourceLine(string line)
        {
            ConcurrentWriter.WriteLine(line ?? string.Empty);
        }

        /// <summary>
        /// Writes a pre-formatted underline or help/note annotation line in the given foreground color.
        /// </summary>
        public static void DiagnosticAnnotationLine(ConsoleColor foreground, string line)
        {
            ConcurrentWriter.WriteLine(foreground, line ?? string.Empty);
        }

        public static void Display(string msg, params object[]? args)
        {
            ConcurrentWriter.WriteLine(
                Message.LocalizeStringFormat(msg, args)
            );
        }

        public static void Info(string msg, params object[]? args)
        {
            ConcurrentWriter.WriteLine(
                ConsoleColor.Blue,
                Message.LocalizeStringFormat(msg, args)
            );
        }

        public static void Warn(string msg, params object[]? args)
        {
            ConcurrentWriter.WriteLine(
                ConsoleColor.Yellow,
                Message.LocalizeStringFormat(msg, args)
            );
        }

        public static void Error(string msg, params object[]? args)
        {
            ConcurrentWriter.WriteLine(
                ConsoleColor.Red,
                Message.LocalizeStringFormat(msg, args)
            );
        }

        public static void Success(string msg, params object[]? args)
        {
            ConcurrentWriter.WriteLine(
                ConsoleColor.Green,
                Message.LocalizeStringFormat(msg, args)
            );
        }

        public static void Debug(string msg, params object[]? args)
        {
            // It is important to note that we do not localize debug strings.
            ConcurrentWriter.WriteLine(
                ConsoleColor.Magenta,
                msg,
                args ?? []
            );
        }
    }
}