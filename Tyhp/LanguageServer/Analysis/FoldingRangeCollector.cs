namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Collects foldable regions from an AST and from multi-line comments / import groups.
    /// </summary>
    internal static class FoldingRangeCollector
    {
        /// <summary>
        /// Folding ranges for <paramref name="ast"/> and <paramref name="content"/>.
        /// Ranges that span only one line are omitted.
        /// </summary>
        public static FoldingRange[] Collect(SrcFileAst? ast, string content)
        {
            content ??= string.Empty;
            var ranges = new List<FoldingRange>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddCommentRanges(content, ranges, seen);
            AddImportRanges(content, ranges, seen);
            if (ast is not null)
            {
                Walk(ast, ranges, seen);
            }

            return [.. ranges];
        }

        private static void Walk(IBase2Ast node, List<FoldingRange> ranges, HashSet<string> seen)
        {
            if (IsFoldableConstruct(node))
            {
                TryAdd(ranges, seen, PositionUtilities.ToLspRange(node), FoldingRangeKind.Region);
            }

            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    Walk(child, ranges, seen);
                }
            }
        }

        private static bool IsFoldableConstruct(IBase2Ast node)
        {
            return node is PhpObjectTypeDeclAst
                or TyhpStructDeclAst
                or TyhpExtensionDeclAst
                or PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpInlineFunctionAst
                or TyhpAsyncBlockAst
                or PhpIfAst
                or PhpLoopAst
                or PhpConditionalAst
                or PhpTryCatchAst
                or PhpCatchClauseAst
                or TyhpUsingBlockAst
                or PhpStatementBlockAst
                or PhpClassBodyAst
                or PhpArrayAst
                or PhpBlockNamespaceDeclAst
                or PhpNamespaceDeclAst { TopStatements: not null };
        }

        private static void AddCommentRanges(string content, List<FoldingRange> ranges, HashSet<string> seen)
        {
            int i = 0;
            int length = content.Length;
            bool inSingle = false;
            bool inDouble = false;
            while (i < length)
            {
                char c = content[i];
                if (inSingle)
                {
                    if (c == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (c == '\'')
                    {
                        inSingle = false;
                    }

                    i++;
                    continue;
                }

                if (inDouble)
                {
                    if (c == '\\' && i + 1 < length)
                    {
                        i += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        inDouble = false;
                    }

                    i++;
                    continue;
                }

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

                if (c == '/' && i + 1 < length && content[i + 1] == '*')
                {
                    int start = i;
                    i += 2;
                    while (i + 1 < length && !(content[i] == '*' && content[i + 1] == '/'))
                    {
                        i++;
                    }

                    if (i + 1 < length)
                    {
                        i += 2;
                    }

                    Position startPos = PositionUtilities.GetPosition(content, start);
                    Position endPos = PositionUtilities.GetPosition(content, Math.Min(i, length));
                    TryAdd(ranges, seen, startPos, endPos, FoldingRangeKind.Comment);
                    continue;
                }

                if (c == '/' && i + 1 < length && content[i + 1] == '/')
                {
                    while (i < length && content[i] != '\n' && content[i] != '\r')
                    {
                        i++;
                    }

                    continue;
                }

                i++;
            }
        }

        private static void AddImportRanges(string content, List<FoldingRange> ranges, HashSet<string> seen)
        {
            var lines = SplitLines(content);
            int i = 0;
            while (i < lines.Count)
            {
                if (!IsUseLine(lines[i].Text))
                {
                    i++;
                    continue;
                }

                int start = i;
                int end = i;
                i++;
                while (i < lines.Count)
                {
                    string text = lines[i].Text;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        i++;
                        continue;
                    }

                    if (!IsUseLine(text))
                    {
                        break;
                    }

                    end = i;
                    i++;
                }

                if (end > start)
                {
                    TryAdd(
                        ranges,
                        seen,
                        new Position { Line = start, Character = 0 },
                        new Position { Line = end, Character = Math.Max(0, lines[end].Text.Length) },
                        FoldingRangeKind.Imports);
                }
            }
        }

        private static bool IsUseLine(string line)
        {
            string trimmed = line.TrimStart();
            return trimmed.StartsWith("use ", StringComparison.Ordinal)
                || trimmed.StartsWith("use\\", StringComparison.Ordinal);
        }

        private static List<(string Text, int Offset)> SplitLines(string content)
        {
            var lines = new List<(string Text, int Offset)>();
            int i = 0;
            int length = content.Length;
            int lineStart = 0;
            while (i < length)
            {
                char c = content[i];
                if (c == '\r')
                {
                    lines.Add((content[lineStart..i], lineStart));
                    i++;
                    if (i < length && content[i] == '\n')
                    {
                        i++;
                    }

                    lineStart = i;
                    continue;
                }

                if (c == '\n')
                {
                    lines.Add((content[lineStart..i], lineStart));
                    i++;
                    lineStart = i;
                    continue;
                }

                i++;
            }

            lines.Add((content[lineStart..], lineStart));
            return lines;
        }

        private static void TryAdd(
            List<FoldingRange> ranges,
            HashSet<string> seen,
            ProtocolRange range,
            FoldingRangeKind kind)
        {
            if (range.Start is null || range.End is null)
            {
                return;
            }

            TryAdd(ranges, seen, range.Start, range.End, kind);
        }

        private static void TryAdd(
            List<FoldingRange> ranges,
            HashSet<string> seen,
            Position start,
            Position end,
            FoldingRangeKind kind)
        {
            int startLine = start.Line;
            int endLine = end.Line;
            if (end.Character == 0 && endLine > startLine)
            {
                endLine--;
            }

            if (endLine <= startLine)
            {
                return;
            }

            string key = startLine + ":" + endLine + ":" + kind;
            if (!seen.Add(key))
            {
                return;
            }

            ranges.Add(new FoldingRange
            {
                StartLine = startLine,
                EndLine = endLine,
                Kind = kind,
            });
        }
    }
}
