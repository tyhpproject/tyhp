using System.Text;
using Tyhp.Domain.Diagnostics;

namespace Tyhp.CLI
{
    /// <summary>
    /// Builds rustc-style source snippets (caret/underline + inline labels) for diagnostics.
    /// Writing to the console goes through <see cref="Message"/> so colors stay on the
    /// existing ConcurrentWriter path.
    /// </summary>
    public static class RichDiagnosticRenderer
    {
        private const int MinGutterWidth = 4;

        /// <summary>Tab stop used when expanding source tabs so underlines stay aligned.</summary>
        private const int TabWidth = 4;

        /// <summary>
        /// Longest run of source lines rendered for a single span. Longer spans collapse to their
        /// first and last line so a diagnostic covering a whole type cannot flood the console.
        /// </summary>
        private const int MaxSpanLines = 6;

        /// <summary>
        /// Builds the multi-line source snippet (location arrow, gutter, underlines, help/note)
        /// for a diagnostic. Returns an empty list when no source lines can be resolved.
        /// Does not include the primary severity header line.
        /// </summary>
        /// <param name="diagnostic">The diagnostic to render.</param>
        /// <param name="tryGetSourceLine">
        /// Callback that returns the source text for <c>(fileName, 1-based line)</c>,
        /// or <see langword="null"/> when unavailable.
        /// </param>
        public static IReadOnlyList<string> BuildSnippetLines(
            IDiagnostic diagnostic,
            Func<string, int, string?> tryGetSourceLine)
        {
            var snippet = BuildSnippet(diagnostic, tryGetSourceLine);
            var lines = new List<string>(snippet.Count);
            foreach (var line in snippet)
            {
                lines.Add(line.Text);
            }

            return lines;
        }

        /// <summary>
        /// Writes a diagnostic to the console: always the single-line header; when
        /// <paramref name="quiet"/> is false, also writes the rich source snippet (if available).
        /// </summary>
        public static void Write(
            IDiagnostic diagnostic,
            bool quiet,
            Func<string, int, string?>? tryGetSourceLine = null)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            WriteHeader(diagnostic);

            if (quiet)
            {
                return;
            }

            tryGetSourceLine ??= DefaultTryGetSourceLine;
            var snippet = BuildSnippet(diagnostic, tryGetSourceLine);
            if (snippet.Count == 0)
            {
                return;
            }

            var color = GetSeverityColor(diagnostic.Severity);
            foreach (var line in snippet)
            {
                // Underlines and help/note get severity coloring; source lines stay default.
                if (line.IsAnnotation)
                {
                    Message.DiagnosticAnnotationLine(color, line.Text);
                }
                else
                {
                    Message.DiagnosticSourceLine(line.Text);
                }
            }
        }

        /// <summary>
        /// Builds the snippet rows together with the flag that decides how each row is colored.
        /// </summary>
        internal static IReadOnlyList<SnippetLine> BuildSnippet(
            IDiagnostic diagnostic,
            Func<string, int, string?> tryGetSourceLine)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            ArgumentNullException.ThrowIfNull(tryGetSourceLine);

            var lines = new List<SnippetLine>();
            var annotations = CollectAnnotations(diagnostic);

            // Group annotations by file so multi-file secondaries get their own blocks.
            foreach (var fileGroup in annotations.GroupBy(a => a.Span.FileName, StringComparer.Ordinal))
            {
                var fileName = fileGroup.Key;
                if (string.IsNullOrWhiteSpace(fileName) || fileName == "_")
                {
                    continue;
                }

                var fileAnnotations = fileGroup.ToList();
                var neededLines = fileAnnotations
                    .SelectMany(RenderedLineNumbers)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                var sourceByLine = new Dictionary<int, string>();
                foreach (var lineNumber in neededLines)
                {
                    var source = tryGetSourceLine(fileName, lineNumber);
                    if (source != null)
                    {
                        sourceByLine[lineNumber] = source.TrimEnd('\r');
                    }
                }

                if (sourceByLine.Count == 0)
                {
                    continue;
                }

                var renderedLineNumbers = sourceByLine.Keys.OrderBy(n => n).ToList();

                // The gutter grows with the widest line number so every bar stays in one column.
                var gutterWidth = Math.Max(
                    MinGutterWidth,
                    renderedLineNumbers[^1].ToString().Length);
                var gutter = new string(' ', gutterWidth);

                // Location arrow points at the first annotation in this file (primary when present).
                var anchor = fileAnnotations[0].Span;
                lines.Add(new SnippetLine(
                    Message.Localize(
                        "CLI_DiagnosticLocationArrow",
                        fileName,
                        anchor.Line,
                        anchor.Column),
                    IsAnnotation: false));
                lines.Add(new SnippetLine(
                    Message.Localize("CLI_DiagnosticGutterEmpty", gutter),
                    IsAnnotation: false));

                for (var i = 0; i < renderedLineNumbers.Count; i++)
                {
                    var lineNumber = renderedLineNumbers[i];
                    // Insert a blank gutter row when lines are non-contiguous.
                    if (i > 0 && lineNumber > renderedLineNumbers[i - 1] + 1)
                    {
                        lines.Add(new SnippetLine(
                            Message.Localize("CLI_DiagnosticGutterEmpty", gutter),
                            IsAnnotation: false));
                    }

                    var sourceText = sourceByLine[lineNumber];
                    lines.Add(new SnippetLine(
                        Message.Localize(
                            "CLI_DiagnosticGutterLine",
                            FormatGutterNumber(lineNumber, gutterWidth),
                            ExpandTabs(sourceText)),
                        IsAnnotation: false));

                    foreach (var annotation in fileAnnotations.Where(a => SpansLine(a.Span, lineNumber)))
                    {
                        var underline = BuildUnderline(annotation, lineNumber, sourceText);
                        if (underline.Length == 0)
                        {
                            continue;
                        }

                        lines.Add(new SnippetLine(
                            Message.Localize("CLI_DiagnosticGutterUnderline", gutter, underline),
                            IsAnnotation: true));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.Help))
            {
                lines.Add(new SnippetLine(
                    Message.Localize("CLI_DiagnosticHelp", diagnostic.Help),
                    IsAnnotation: true));
            }

            // Story 14 Phase 3: machine-applicable suggestions surface as help: hints when the
            // producer did not already put the same text in Help (quiet mode skips this path).
            if (diagnostic.Suggestion is { } suggestion)
            {
                var hint = !string.IsNullOrWhiteSpace(suggestion.Description)
                    ? suggestion.Description!
                    : Message.Localize("CLI_DiagnosticDidYouMean", suggestion.Replacement);

                if (!string.IsNullOrWhiteSpace(hint)
                    && !string.Equals(diagnostic.Help, hint, StringComparison.Ordinal))
                {
                    lines.Add(new SnippetLine(
                        Message.Localize("CLI_DiagnosticHelp", hint),
                        IsAnnotation: true));
                }
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.Note))
            {
                lines.Add(new SnippetLine(
                    Message.Localize("CLI_DiagnosticNote", diagnostic.Note),
                    IsAnnotation: true));
            }

            return lines;
        }

        private static void WriteHeader(IDiagnostic diagnostic)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error:
                    Message.TyhpError(
                        diagnostic.FileName,
                        diagnostic.Line,
                        diagnostic.Column,
                        (int)diagnostic.Code,
                        diagnostic.FormatParams);
                    break;

                case DiagnosticSeverity.Warning:
                    Message.TyhpWarn(
                        diagnostic.FileName,
                        diagnostic.Line,
                        diagnostic.Column,
                        (int)diagnostic.Code,
                        diagnostic.FormatParams);
                    break;

                case DiagnosticSeverity.Info:
                case DiagnosticSeverity.Hint:
                    Message.TyhpInfo(
                        diagnostic.FileName,
                        diagnostic.Line,
                        diagnostic.Column,
                        (int)diagnostic.Code,
                        diagnostic.FormatParams);
                    break;
            }
        }

        private static List<SpanAnnotation> CollectAnnotations(IDiagnostic diagnostic)
        {
            var list = new List<SpanAnnotation>();

            // File-level diagnostics (reported with line 0) have no construct to point at.
            if (diagnostic.HasSourcePosition)
            {
                list.Add(new SpanAnnotation(
                    DiagnosticSpan.FromDiagnostic(diagnostic),
                    Label: null,
                    IsPrimary: true));
            }

            if (diagnostic.Labels is { Count: > 0 })
            {
                foreach (var label in diagnostic.Labels)
                {
                    list.Add(new SpanAnnotation(label.Span, Label: label.Message, IsPrimary: false));
                }
            }

            return list;
        }

        /// <summary>
        /// Source line numbers to render for an annotation, collapsing over-long spans to their
        /// first and last line (the gap then renders as a blank gutter row).
        /// </summary>
        private static IEnumerable<int> RenderedLineNumbers(SpanAnnotation annotation)
        {
            var start = annotation.Span.Line;
            var end = Math.Max(start, annotation.Span.EffectiveEndLine);

            if (end - start + 1 > MaxSpanLines)
            {
                yield return start;
                yield return end;
                yield break;
            }

            for (var lineNumber = start; lineNumber <= end; lineNumber++)
            {
                yield return lineNumber;
            }
        }

        private static bool SpansLine(DiagnosticSpan span, int lineNumber)
            => lineNumber >= span.Line && lineNumber <= span.EffectiveEndLine;

        private static string BuildUnderline(SpanAnnotation annotation, int lineNumber, string sourceText)
        {
            var span = annotation.Span;
            int startCol;
            int endCol;

            if (span.Line == span.EffectiveEndLine)
            {
                startCol = span.Column;
                endCol = span.EndColumn.HasValue
                    ? Math.Max(span.Column + 1, span.EndColumn.Value)
                    : span.Column + 1;
            }
            else if (lineNumber == span.Line)
            {
                startCol = span.Column;
                endCol = Math.Max(startCol + 1, sourceText.Length);
            }
            else if (lineNumber == span.EffectiveEndLine)
            {
                startCol = 0;
                endCol = Math.Max(1, span.EffectiveEndColumn);
            }
            else
            {
                startCol = 0;
                endCol = Math.Max(1, sourceText.Length);
            }

            // Clamp to the line so a stale end position cannot trail off past the source text;
            // one column past the last character is kept so end-of-line spans still get a caret.
            startCol = Math.Clamp(startCol, 0, sourceText.Length);
            endCol = Math.Clamp(endCol, startCol + 1, Math.Max(startCol + 1, sourceText.Length));

            var displayStart = ToDisplayColumn(sourceText, startCol);
            var displayWidth = Math.Max(1, ToDisplayColumn(sourceText, endCol) - displayStart);

            var mark = annotation.IsPrimary ? '^' : '-';
            var sb = new StringBuilder();
            sb.Append(' ', displayStart);
            sb.Append(mark, displayWidth);

            if (!string.IsNullOrEmpty(annotation.Label))
            {
                sb.Append(' ');
                sb.Append(annotation.Label);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Replaces tabs with spaces so the rendered line and its underline use the same tab stops.
        /// </summary>
        private static string ExpandTabs(string sourceText)
        {
            if (!sourceText.Contains('\t', StringComparison.Ordinal))
            {
                return sourceText;
            }

            var sb = new StringBuilder(sourceText.Length + TabWidth);
            foreach (var ch in sourceText)
            {
                if (ch == '\t')
                {
                    sb.Append(' ', TabWidth - (sb.Length % TabWidth));
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts a 0-based character offset into the column it occupies after
        /// <see cref="ExpandTabs"/>.
        /// </summary>
        private static int ToDisplayColumn(string sourceText, int column)
        {
            var display = 0;
            var limit = Math.Min(column, sourceText.Length);
            for (var i = 0; i < limit; i++)
            {
                display += sourceText[i] == '\t'
                    ? TabWidth - (display % TabWidth)
                    : 1;
            }

            // Offsets past the end of the line map one-to-one.
            return display + Math.Max(0, column - limit);
        }

        private static string FormatGutterNumber(int lineNumber, int gutterWidth)
            => lineNumber.ToString().PadLeft(gutterWidth);

        private static ConsoleColor GetSeverityColor(DiagnosticSeverity severity)
            => severity switch
            {
                DiagnosticSeverity.Error => ConsoleColor.Red,
                DiagnosticSeverity.Warning => ConsoleColor.Yellow,
                _ => ConsoleColor.Blue,
            };

        private static string? DefaultTryGetSourceLine(string fileName, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName is "_" or "<input>"
                || lineNumber < 1)
            {
                return null;
            }

            try
            {
                if (!File.Exists(fileName))
                {
                    return null;
                }

                // Stream line-by-line to avoid loading huge files for a single diagnostic.
                var current = 0;
                foreach (var line in File.ReadLines(fileName))
                {
                    current++;
                    if (current == lineNumber)
                    {
                        return line;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// One rendered snippet row. <paramref name="IsAnnotation"/> marks underline and help/note
        /// rows, which are colored by severity; source and gutter rows keep the default colors.
        /// </summary>
        internal readonly record struct SnippetLine(string Text, bool IsAnnotation);

        private readonly record struct SpanAnnotation(
            DiagnosticSpan Span,
            string? Label,
            bool IsPrimary);
    }
}
