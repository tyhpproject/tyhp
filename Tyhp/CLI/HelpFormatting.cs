namespace Tyhp.CLI
{
    /// <summary>
    /// Shared helpers for consistent CLI help text formatting.
    /// Callers pass localization resource keys; identifiers such as flag names stay as literals.
    /// </summary>
    static class HelpFormatting
    {
        private const int FlagColumnWidth = 28;
        private const int ExampleCommandWidth = 42;
        private const int DefaultWrapWidth = 80;

        /// <summary>
        /// Resolves the executable name shown in usage lines (handles <c>dotnet …dll</c>).
        /// </summary>
        public static string GetExecutableName()
        {
            string executable = Path.GetFileName(Environment.GetCommandLineArgs()[0]) ?? "tyhp";
            if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                executable = "dotnet " + executable;
            }

            return executable;
        }

        /// <summary>
        /// Prints a blank line and a colored section header.
        /// </summary>
        /// <param name="titleKey">Resource key for the section title.</param>
        /// <param name="args">Optional format arguments for the title.</param>
        public static void Section(string titleKey, params object[]? args)
        {
            Message.Display("");
            Message.Info(titleKey, args);
        }

        /// <summary>
        /// Prints a usage line. <paramref name="usageTemplateKey"/> must contain <c>{0}</c> for the executable.
        /// </summary>
        public static void Usage(string executable, string usageTemplateKey)
        {
            Message.Display(usageTemplateKey, executable);
        }

        /// <summary>
        /// Prints an aligned option line: padded flag column plus localized description.
        /// </summary>
        /// <param name="flag">Literal flag text (e.g. <c>--json</c>).</param>
        /// <param name="descriptionKey">Resource key for the description.</param>
        public static void Option(string flag, string descriptionKey)
        {
            var flagColumn = flag.Length >= FlagColumnWidth
                ? flag + "  "
                : flag.PadRight(FlagColumnWidth);

            // LocalizeRaw skips String.Format: descriptions take no arguments, and some document
            // config values that contain braces (e.g. a psr4 map) which would otherwise throw.
            Message.Display(
                "CLI_HelpOptionLine",
                flagColumn,
                Message.LocalizeRaw(descriptionKey));
        }

        /// <summary>
        /// Prints an example command with a localized description.
        /// </summary>
        /// <param name="command">Full example command (may include the executable name).</param>
        /// <param name="descriptionKey">Resource key for the description.</param>
        public static void Example(string command, string descriptionKey)
        {
            // PadRight is a no-op when the command is already wider than the column, so add a
            // small gap so the description does not run into the command text.
            var commandColumn = command.Length >= ExampleCommandWidth
                ? command + "  "
                : command.PadRight(ExampleCommandWidth);

            Message.Display(
                "CLI_HelpExampleLine",
                commandColumn,
                Message.LocalizeRaw(descriptionKey));
        }

        /// <summary>
        /// Prints a paragraph with simple word wrapping.
        /// </summary>
        /// <param name="textKey">Resource key for the paragraph text.</param>
        /// <param name="args">Optional format arguments.</param>
        public static void Paragraph(string textKey, params object[]? args)
        {
            var text = Message.Localize(textKey, args ?? Array.Empty<object>());
            foreach (var line in WrapWords(text, GetWrapWidth()))
            {
                Message.Display("CLI_HelpWrappedLine", line);
            }
        }

        private static int GetWrapWidth()
        {
            try
            {
                var width = Console.WindowWidth;
                if (width > 20)
                {
                    return Math.Min(width - 1, 100);
                }
            }
            catch (IOException)
            {
                // stdout redirected or no console — use default
            }

            return DefaultWrapWidth;
        }

        private static IEnumerable<string> WrapWords(string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield return string.Empty;
                yield break;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    current.Append(word);
                    continue;
                }

                if (current.Length + 1 + word.Length <= maxWidth)
                {
                    current.Append(' ').Append(word);
                    continue;
                }

                yield return current.ToString();
                current.Clear();
                current.Append(word);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }
    }
}
