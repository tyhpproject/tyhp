using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;

namespace Tyhp.CLI
{
    /// <summary>
    /// Prints the long-form explanation for a diagnostic code (<c>tyhp --explain TYHP####</c>).
    /// </summary>
    public class ExplainAction : ActionRunnerBase
    {
        private readonly Config.Project _project;

        public ExplainAction(Config.Project project)
        {
            this._project = project;
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            var token = ResolveCodeToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                Message.Error("CLI_ExplainMissingCode");
                Message.Display("CLI_ExplainMissingCodeHint", HelpFormatting.GetExecutableName());
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            if (!MessageCodeCatalog.TryParseToken(token, out var code)
                || !MessageCodeCatalog.TryGet(code, out var entry))
            {
                Message.Error("CLI_ExplainUnknownCode", token.Trim());
                Environment.ExitCode = (int)ExitCode.GenericError;
                return null;
            }

            PrintExplanation(entry);
            Environment.ExitCode = (int)ExitCode.Success;
            return null;
        }

        /// <summary>
        /// Formats the human-readable explanation lines for a catalog entry (testable without I/O).
        /// </summary>
        public static IReadOnlyList<string> FormatExplanationLines(MessageCodeEntry entry)
        {
            var codeId = MessageCodeCatalog.FormatCode(entry.Code);
            var severityLabel = string.Join(
                ", ",
                entry.Variants.Select(v => LocalizeSeverity(v.Severity)));
            var categoryName = MessageCodeCatalog.LocalizeCategory(entry.Category);
            var longForm = MessageCodeCatalog.ResolveLongForm(entry);

            var lines = new List<string>
            {
                Message.Localize("CLI_ExplainTitle", codeId, severityLabel, entry.Name),
                string.Empty,
                Message.Localize("CLI_ExplainMessageHeader"),
            };

            lines.AddRange(FormatMessageLines(entry));
            lines.Add(string.Empty);
            lines.Add(Message.Localize("CLI_ExplainCategoryHeader"));
            lines.Add("  " + categoryName);
            lines.Add(string.Empty);
            lines.Add(Message.Localize("CLI_ExplainBodyHeader"));

            foreach (var block in MessageCodeCatalog.SplitLongFormBlocks(longForm))
            {
                if (block.IsCodeFence)
                {
                    foreach (var fenceLine in block.Text.Split('\n'))
                    {
                        lines.Add("  " + fenceLine);
                    }
                }
                else
                {
                    lines.Add("  " + block.Text);
                }
            }

            lines.Add(string.Empty);
            lines.Add(Message.Localize("CLI_ExplainDocsHint", codeId));
            return lines;
        }

        /// <summary>
        /// Renders the short message for each severity the code carries text for, labeling them
        /// only when the texts actually differ.
        /// </summary>
        private static IEnumerable<string> FormatMessageLines(MessageCodeEntry entry)
        {
            var distinct = entry.Variants
                .Select(v => v.ShortMessage)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinct.Count == 1)
            {
                return ["  " + distinct[0]];
            }

            return entry.Variants.Select(
                v => "  " + LocalizeSeverity(v.Severity) + ": " + v.ShortMessage);
        }

        private string? ResolveCodeToken()
        {
            var fromFlag = this._project.GetConfigValue("code");
            if (!string.IsNullOrWhiteSpace(fromFlag))
            {
                return fromFlag;
            }

            return this._project.ExplicitPaths.Count > 0
                ? this._project.ExplicitPaths[0]
                : null;
        }

        private static void PrintExplanation(MessageCodeEntry entry)
        {
            // FormatExplanationLines returns text that is already localized, and short messages still
            // carry their `{0}` placeholders — so these must not go through a Message overload that
            // treats the line as a resource key / format string.
            foreach (var line in FormatExplanationLines(entry))
            {
                Message.DiagnosticSourceLine(line);
            }
        }

        private static string LocalizeSeverity(DiagnosticSeverity severity)
            => severity switch
            {
                DiagnosticSeverity.Warning => Message.Localize("warning"),
                DiagnosticSeverity.Info => Message.Localize("info"),
                DiagnosticSeverity.Hint => Message.Localize("info"),
                _ => Message.Localize("error"),
            };

    }
}
