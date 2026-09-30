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

        public static void WriteManualCredit(string outputDirectory, string extName)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
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
                    WriteNotice(outputDirectory, corpora);
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
                WriteNotice(outputDirectory, corpora);
            }
            catch (IOException)
            {
            }
        }

        private static void WriteNotice(string outputDirectory, IReadOnlyList<StubCorpusDescriptor> corpora)
        {
            try
            {
                var path = Path.Combine(outputDirectory, NoticeFileName);
                var existing = File.Exists(path) ? File.ReadAllText(path) : "";
                if (existing.Contains("Stub corpora", StringComparison.Ordinal))
                {
                    return;
                }

                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    lines.Add(existing.TrimEnd());
                    lines.Add("");
                }

                lines.Add("Stub corpora (Layer 2 tyhpdef harvest)");
                foreach (var corpus in corpora)
                {
                    lines.Add($"- {corpus.Title}: {corpus.License} ({corpus.PageUrl})");
                }

                File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }
}
