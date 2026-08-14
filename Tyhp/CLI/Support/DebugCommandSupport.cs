using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.CLI.Support
{
    /// <summary>
    /// Shared plumbing for the <c>tokenize</c> and <c>dump-ast</c> debug commands:
    /// input discovery, parse-mode resolution, and JSON output writing.
    /// </summary>
    public static class DebugCommandSupport
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        /// <summary>
        /// Resolves the source files to operate on. Explicit CLI paths take priority; otherwise
        /// the project's configured source globs are used.
        /// </summary>
        public static List<string> ResolveInputFiles(Config.Project project, DiagnosticBag discoveryDiagnostics)
        {
            if (project.ExplicitPaths.Count > 0)
            {
                return Config.SourceFileDiscovery
                    .FromExplicitPaths(project.ExplicitPaths, discoveryDiagnostics)
                    .ToList();
            }

            return project.GetProjectSourceFiles().ToList();
        }

        /// <summary>
        /// Determines the parse mode for a file. An explicit <c>--mode</c> override wins; otherwise
        /// the mode is inferred from the file extension (<c>.tyhpdef</c>, <c>.tyhp</c>, else PHP).
        /// </summary>
        public static ParseMode ResolveParseMode(string filePath, string? modeOverride)
        {
            if (!string.IsNullOrWhiteSpace(modeOverride))
            {
                switch (modeOverride.Trim().ToLowerInvariant())
                {
                    case "php":
                        return ParseMode.Php;
                    case "tyhp":
                        return ParseMode.Tyhp;
                    case "tyhpdef":
                        return ParseMode.Tyhpdef;
                }
            }

            // Check .tyhpdef before .tyhp because ".tyhpdef" also ends with ".tyhp".
            if (filePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return ParseMode.Tyhpdef;
            }

            if (filePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            {
                return ParseMode.Tyhp;
            }

            return ParseMode.Php;
        }

        /// <summary>
        /// Writes a localized status/progress line to standard error. Debug commands keep
        /// standard output reserved for the JSON document so callers can redirect it cleanly
        /// (e.g. <c>tyhp tokenize file.tyhp &gt; tokens.json</c>).
        /// </summary>
        public static void Status(string messageKey, params object[] args)
        {
            Console.Error.WriteLine(Message.Localize(messageKey, args));
        }

        /// <summary>
        /// Writes the JSON document either to <paramref name="outputPath"/> (when provided) or to
        /// standard output. When writing to a file, a localized status line is emitted to stderr.
        /// </summary>
        public static void WriteJson(JsonNode root, string? outputPath)
        {
            var json = root.ToJsonString(JsonOptions);

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                File.WriteAllText(outputPath, json);
                Status("CLI_DebugOutputWritten", outputPath);
                return;
            }

            Console.Out.WriteLine(json);
        }
    }
}
