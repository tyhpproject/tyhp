namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Writes and extends <c>{output}/SOURCES.md</c> for php.net manual credit and Layer 2 stubs.
    /// Layer 2 <b>appends</b>; it never wipes an existing PHP Documentation Group section.
    /// </summary>
    internal static class TyhpdefSourcesMarkdown
    {
        public const string FileName = "SOURCES.md";
        public const string NoticeFileName = "NOTICE";
        public const string StubHeading = "## Stub corpora (Layer 2)";

        internal const string ManualNoticeHeading = "PHP Documentation Group manual (CC BY 3.0)";
        internal const string StubNoticeHeading = "Stub corpora (Layer 2 tyhpdef harvest)";

        private static readonly string[] NoticeHeadings = [ManualNoticeHeading, StubNoticeHeading];

        public static void WriteManualCredit(string outputDirectory, string extName)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                WriteManualNotice(outputDirectory);
                var path = Path.Combine(outputDirectory, FileName);
                var existing = File.Exists(path) ? File.ReadAllText(path) : "";
                if (existing.Contains("PHP Documentation Group", StringComparison.Ordinal))
                {
                    EnsureExtensionBullet(path, existing, extName);
                    return;
                }
                var manual = string.Join(
                    Environment.NewLine,
                    [
                        "# Sources for this tyhpdef generation",
                        "",
                        $"- Extension: `{extName}`",
                        "- PHP Documentation Group HTML manual (`php_manual_*.html.gz`) — [CC BY 3.0](https://www.php.net/manual/en/cc.license.php)",
                        "- Catalog: see the Tyhp repository `THIRD_PARTY.md`",
                        "",
                    ]);

                if (existing.Contains(StubHeading, StringComparison.Ordinal))
                {
                    // Stubs wrote first (unusual); prepend manual credit and keep the stub section.
                    var stubIndex = existing.IndexOf(StubHeading, StringComparison.Ordinal);
                    File.WriteAllText(path, manual + existing[stubIndex..]);
                    return;
                }

                File.WriteAllText(path, manual);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Shared output dirs (e.g. <c>tyhpdef/php</c> with 11 always-present extensions)
        /// keep one <c>SOURCES.md</c>. Later Reflection harvests add an <c>- Extension:</c>
        /// bullet instead of skipping the file.
        /// </summary>
        internal static void EnsureExtensionBullet(string path, string existing, string extName)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(extName))
            {
                return;
            }

            var quoted = "`" + extName.Trim() + "`";
            var newline = existing.Contains('\r') ? "\r\n" : "\n";
            var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
            var lastExtensionLine = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("- Extension:", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("- Extensions:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                lastExtensionLine = i;
                if (trimmed.Contains(quoted, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var bullet = "- Extension: " + quoted;
            if (lastExtensionLine >= 0)
            {
                lines.Insert(lastExtensionLine + 1, bullet);
            }
            else
            {
                var insertAt = 0;
                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("# ", StringComparison.Ordinal))
                    {
                        insertAt = i + 1;
                        while (insertAt < lines.Count && string.IsNullOrWhiteSpace(lines[insertAt]))
                        {
                            insertAt++;
                        }

                        break;
                    }
                }

                lines.Insert(insertAt, bullet);
            }

            var text = string.Join(newline, lines);
            if (!text.EndsWith(newline, StringComparison.Ordinal) && existing.EndsWith('\n'))
            {
                text += newline;
            }

            File.WriteAllText(path, text);
        }

        public static void AppendStubCorpora(
            string outputDirectory,
            string extName,
            IReadOnlyList<StubCorpusDescriptor> corpora)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || corpora.Count == 0)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                var path = Path.Combine(outputDirectory, FileName);
                var existing = File.Exists(path) ? File.ReadAllText(path) : "";
                if (existing.Contains(StubHeading, StringComparison.Ordinal))
                {
                    WriteStubNotice(outputDirectory, corpora);
                    return;
                }

                var lines = new List<string>();
                if (string.IsNullOrWhiteSpace(existing))
                {
                    lines.Add("# Sources for this tyhpdef generation");
                    lines.Add("");
                    lines.Add($"- Extension: `{extName}`");
                    lines.Add("- Catalog: see the Tyhp repository `THIRD_PARTY.md`");
                    lines.Add("");
                }
                else
                {
                    lines.Add(existing.TrimEnd());
                    lines.Add("");
                }

                lines.Add(StubHeading);
                lines.Add("");
                lines.Add("Harvested into `overlays/stubs/`. Raw stub trees are not committed.");
                lines.Add("");
                foreach (var corpus in corpora)
                {
                    lines.Add($"- {corpus.Title} ({corpus.License}) — {corpus.PageUrl}");
                }

                lines.Add("");
                File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                WriteStubNotice(outputDirectory, corpora);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Writes the attribution required by the PHP manual's CC BY 3.0 license into
        /// <c>NOTICE</c>. Safe to repeat: the section is replaced, not duplicated.
        /// </summary>
        internal static void WriteManualNotice(string outputDirectory)
        {
            UpsertNoticeSection(
                outputDirectory,
                ManualNoticeHeading,
                [
                    "Documentation comments in these definitions are adapted from the PHP manual.",
                    "Copyright © The PHP Documentation Group.",
                    "Licensed under the Creative Commons Attribution 3.0 License:",
                    "https://creativecommons.org/licenses/by/3.0/",
                    "Source: https://www.php.net/manual/en/copyright.php",
                ]);
        }

        /// <summary>
        /// Lists each stub corpus and reproduces its copyright and permission text, which
        /// the MIT and Apache-2.0 licenses require to accompany adapted material.
        /// Safe to repeat: the section is replaced, not duplicated.
        /// </summary>
        internal static void WriteStubNotice(string outputDirectory, IReadOnlyList<StubCorpusDescriptor> corpora)
        {
            var body = new List<string>();
            foreach (var corpus in corpora)
            {
                body.Add($"- {corpus.Title}: {corpus.License} ({corpus.PageUrl})");
            }

            body.Add("");
            body.Add("Material adapted from these projects stays under their licenses:");
            foreach (var corpus in corpora)
            {
                body.Add("");
                body.Add($"{corpus.Title} ({corpus.License})");
                body.Add("");
                body.AddRange(corpus.LicenseNotice.Replace("\r\n", "\n").TrimEnd().Split('\n'));
            }

            UpsertNoticeSection(outputDirectory, StubNoticeHeading, body);
        }

        /// <summary>
        /// Replaces the section that starts at <paramref name="heading"/> and keeps every other
        /// part of <c>NOTICE</c> (hand-written text and the other generated section) as it was.
        /// Sections are written in a fixed order, so the output does not depend on call order.
        /// </summary>
        private static void UpsertNoticeSection(string outputDirectory, string heading, IReadOnlyList<string> bodyLines)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                var path = Path.Combine(outputDirectory, NoticeFileName);
                var existing = File.Exists(path) ? File.ReadAllText(path) : "";

                var prefix = new List<string>();
                var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var current = prefix;
                foreach (var line in existing.Replace("\r\n", "\n").Split('\n'))
                {
                    var trimmedEnd = line.TrimEnd();
                    if (Array.IndexOf(NoticeHeadings, trimmedEnd) >= 0)
                    {
                        current = [];
                        sections[trimmedEnd] = current;
                    }

                    current.Add(line);
                }

                List<string> replacement = [heading, .. bodyLines];
                sections[heading] = replacement;

                var output = new List<string>();
                AppendNoticeBlock(output, prefix);
                foreach (var known in NoticeHeadings)
                {
                    if (sections.TryGetValue(known, out var section))
                    {
                        AppendNoticeBlock(output, section);
                    }
                }

                File.WriteAllText(path, string.Join(Environment.NewLine, output) + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }

        private static void AppendNoticeBlock(List<string> output, List<string> block)
        {
            var start = 0;
            var end = block.Count;
            while (start < end && string.IsNullOrWhiteSpace(block[start]))
            {
                start++;
            }

            while (end > start && string.IsNullOrWhiteSpace(block[end - 1]))
            {
                end--;
            }

            if (start == end)
            {
                return;
            }

            if (output.Count > 0)
            {
                output.Add("");
            }

            for (var i = start; i < end; i++)
            {
                output.Add(block[i].TrimEnd());
            }
        }
    }
}
