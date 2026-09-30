namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Basic document formatting: import sorting and indentation normalization.
    /// Full pretty-printing is <c>PLACEHOLDER_STORY_30: advanced code formatting</c>.
    /// </summary>
    internal static class DocumentFormatter
    {
        /// <summary>
        /// Formats <paramref name="content"/>. When <paramref name="range"/> is set,
        /// only lines that intersect that range are rewritten.
        /// </summary>
        public static TextEdit[] Format(string content, FormattingOptions? options, ProtocolRange? range)
        {
            content ??= string.Empty;
            if (content.Length == 0)
            {
                return [];
            }

            int tabSize = options?.TabSize > 0 ? options.TabSize : 4;
            bool insertSpaces = options?.InsertSpaces ?? true;
            string working = range is null ? UseStatementEdits.SortImports(content) : content;
            string formatted = NormalizeIndent(working, tabSize, insertSpaces);

            if (range is { Start: not null, End: not null })
            {
                formatted = MergeIndentRange(content, formatted, range);
            }

            if (string.Equals(formatted, content, StringComparison.Ordinal))
            {
                return [];
            }

            return DiffEdits(content, formatted);
        }

        /// <summary>
        /// PLACEHOLDER_STORY_30: advanced code formatting (brace placement, operator
        /// spacing, blank lines between class members).
        /// </summary>
        private static string NormalizeIndent(string content, int tabSize, bool insertSpaces)
        {
            List<(string Text, string Newline)> lines = UseStatementEdits.SplitLinesKeepNewline(content);
            var output = new List<string>(lines.Count);
            int depth = 0;
            bool inSingle = false;
            bool inDouble = false;
            bool inBlockComment = false;
            bool inHeredoc = false;
            string heredocEnd = string.Empty;

            foreach ((string Text, string Newline) line in lines)
            {
                string text = line.Text;
                bool preserve = inSingle || inDouble || inBlockComment || inHeredoc;
                int lineDepth = depth;
                if (!preserve)
                {
                    string trimmed = text.TrimStart();
                    if (trimmed.Length > 0 && IsCloser(trimmed[0]))
                    {
                        lineDepth = Math.Max(0, depth - 1);
                    }
                }

                if (preserve || string.IsNullOrWhiteSpace(text))
                {
                    output.Add(text + line.Newline);
                }
                else
                {
                    string trimmed = text.TrimStart();
                    string indent = trimmed.Length == 0
                        ? string.Empty
                        : MakeIndent(lineDepth, tabSize, insertSpaces);
                    output.Add(indent + trimmed + line.Newline);
                }

                UpdateScanState(
                    text,
                    ref depth,
                    ref inSingle,
                    ref inDouble,
                    ref inBlockComment,
                    ref inHeredoc,
                    ref heredocEnd);
            }

            return string.Concat(output);
        }

        private static string MergeIndentRange(string original, string formatted, ProtocolRange range)
        {
            List<(string Text, string Newline)> originalLines = UseStatementEdits.SplitLinesKeepNewline(original);
            List<(string Text, string Newline)> formattedLines = UseStatementEdits.SplitLinesKeepNewline(formatted);
            int startLine = range.Start!.Line;
            int endLine = range.End!.Line;
            if (range.End.Character == 0 && endLine > startLine)
            {
                endLine--;
            }

            startLine = Math.Clamp(startLine, 0, Math.Max(0, originalLines.Count - 1));
            endLine = Math.Clamp(endLine, startLine, Math.Max(0, originalLines.Count - 1));

            var merged = new List<string>(originalLines.Count);
            for (int i = 0; i < originalLines.Count; i++)
            {
                if (i >= startLine && i <= endLine && i < formattedLines.Count)
                {
                    merged.Add(formattedLines[i].Text + formattedLines[i].Newline);
                }
                else
                {
                    merged.Add(originalLines[i].Text + originalLines[i].Newline);
                }
            }

            return string.Concat(merged);
        }

        internal static TextEdit[] DiffEdits(string original, string formatted)
        {
            if (string.Equals(original, formatted, StringComparison.Ordinal))
            {
                return [];
            }

            List<(string Text, string Newline)> oldLines = UseStatementEdits.SplitLinesKeepNewline(original);
            List<(string Text, string Newline)> newLines = UseStatementEdits.SplitLinesKeepNewline(formatted);
            int prefix = 0;
            int maxPrefix = Math.Min(oldLines.Count, newLines.Count);
            while (prefix < maxPrefix
                && string.Equals(oldLines[prefix].Text + oldLines[prefix].Newline, newLines[prefix].Text + newLines[prefix].Newline, StringComparison.Ordinal))
            {
                prefix++;
            }

            int oldSuffix = oldLines.Count;
            int newSuffix = newLines.Count;
            while (oldSuffix > prefix
                && newSuffix > prefix
                && string.Equals(
                    oldLines[oldSuffix - 1].Text + oldLines[oldSuffix - 1].Newline,
                    newLines[newSuffix - 1].Text + newLines[newSuffix - 1].Newline,
                    StringComparison.Ordinal))
            {
                oldSuffix--;
                newSuffix--;
            }

            int startOffset = 0;
            for (int i = 0; i < prefix; i++)
            {
                startOffset += oldLines[i].Text.Length + oldLines[i].Newline.Length;
            }

            int endOffset = startOffset;
            for (int i = prefix; i < oldSuffix; i++)
            {
                endOffset += oldLines[i].Text.Length + oldLines[i].Newline.Length;
            }

            var replacement = new System.Text.StringBuilder();
            for (int i = prefix; i < newSuffix; i++)
            {
                replacement.Append(newLines[i].Text);
                replacement.Append(newLines[i].Newline);
            }

            return
            [
                new TextEdit
                {
                    Range = new ProtocolRange
                    {
                        Start = PositionUtilities.GetPosition(original, startOffset),
                        End = PositionUtilities.GetPosition(original, endOffset),
                    },
                    NewText = replacement.ToString(),
                },
            ];
        }

        private static void UpdateScanState(
            string line,
            ref int depth,
            ref bool inSingle,
            ref bool inDouble,
            ref bool inBlockComment,
            ref bool inHeredoc,
            ref string heredocEnd)
        {
            int i = 0;
            int length = line.Length;
            if (inHeredoc)
            {
                string trimmed = line.TrimEnd('\r', '\n').Trim();
                if (string.Equals(trimmed, heredocEnd, StringComparison.Ordinal)
                    || string.Equals(trimmed, heredocEnd + ";", StringComparison.Ordinal))
                {
                    inHeredoc = false;
                    heredocEnd = string.Empty;
                }

                return;
            }

            while (i < length)
            {
                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        return;
                    }

                    inBlockComment = false;
                    i = end + 2;
                    continue;
                }

                if (inSingle)
                {
                    if (line[i] == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (line[i] == '\'')
                    {
                        inSingle = false;
                    }

                    i++;
                    continue;
                }

                if (inDouble)
                {
                    if (line[i] == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (line[i] == '"')
                    {
                        inDouble = false;
                    }

                    i++;
                    continue;
                }

                if (i + 1 < length && line[i] == '/' && line[i + 1] == '/')
                {
                    return;
                }

                if (i + 1 < length && line[i] == '/' && line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                if (i + 2 < length && line[i] == '<' && line[i + 1] == '<' && line[i + 2] == '<')
                {
                    int start = i + 3;
                    while (start < length && (line[start] == '\'' || line[start] == '"' || line[start] == ' '))
                    {
                        start++;
                    }

                    int end = start;
                    while (end < length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                    {
                        end++;
                    }

                    if (end > start)
                    {
                        inHeredoc = true;
                        heredocEnd = line[start..end];
                    }

                    return;
                }

                char c = line[i];
                if (c == '\'')
                {
                    inSingle = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inDouble = true;
                    i++;
                    continue;
                }

                if (c is '{' or '[' or '(')
                {
                    depth++;
                }
                else if (c is '}' or ']' or ')' && depth > 0)
                {
                    depth--;
                }

                i++;
            }
        }

        private static bool IsCloser(char c) => c is '}' or ']' or ')';

        private static string MakeIndent(int depth, int tabSize, bool insertSpaces)
        {
            if (depth <= 0)
            {
                return string.Empty;
            }

            if (insertSpaces)
            {
                return new string(' ', depth * tabSize);
            }

            return new string('\t', depth);
        }
    }
}
