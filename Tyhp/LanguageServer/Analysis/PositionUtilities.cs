namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Converts between LSP positions (0-based line, UTF-16 character) and ANTLR/Tyhp
    /// AST coordinates (1-based line, 0-based character column).
    /// </summary>
    public static class PositionUtilities
    {
        /// <summary>
        /// Converts an ANTLR 1-based line and 0-based column to an LSP position.
        /// </summary>
        public static Position ToLspPosition(int antlrLine, int antlrColumn)
        {
            return new Position
            {
                Line = Math.Max(0, antlrLine - 1),
                Character = Math.Max(0, antlrColumn),
            };
        }

        /// <summary>
        /// Converts an LSP position to ANTLR (1-based line, 0-based column).
        /// </summary>
        public static (int Line, int Column) FromLspPosition(Position lspPosition)
        {
            ArgumentNullException.ThrowIfNull(lspPosition);
            return (Math.Max(1, lspPosition.Line + 1), Math.Max(0, lspPosition.Character));
        }

        /// <summary>
        /// Converts an AST node's source span to an LSP range. Missing end positions
        /// are estimated from the last child, then fall back to a zero-width range
        /// at the start.
        /// </summary>
        public static ProtocolRange ToLspRange(IBase2Ast node)
        {
            ArgumentNullException.ThrowIfNull(node);
            var (endLine, endColumn) = ResolveEnd(node);
            int startLine = node.Line < 1 ? 1 : node.Line;
            int startColumn = node.Column < 0 ? 0 : node.Column;
            if (endLine < 1)
            {
                endLine = startLine;
                endColumn = startColumn;
            }

            return new ProtocolRange
            {
                Start = ToLspPosition(startLine, startColumn),
                End = ToLspPosition(endLine, endColumn),
            };
        }

        /// <summary>
        /// Converts a symbol's declaration span to an LSP range. Missing end positions
        /// fall back to a range covering the symbol name at the start position.
        /// </summary>
        public static ProtocolRange ToLspRange(Tyhp.TyhpLang.Binder.Symbols.BaseSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            if (symbol.DeclaringAstNode is IBase2Ast node && node.Line >= 1)
            {
                return ToLspRange(node);
            }

            int startLine = symbol.Line < 1 ? 1 : symbol.Line;
            int startColumn = symbol.Column < 0 ? 0 : symbol.Column;
            int endLine = symbol.EndLine >= 1 ? symbol.EndLine : startLine;
            int endColumn = symbol.EndColumn >= 0
                ? symbol.EndColumn
                : startColumn + Math.Max(1, symbol.Name.Length);
            return new ProtocolRange
            {
                Start = ToLspPosition(startLine, startColumn),
                End = ToLspPosition(endLine, endColumn),
            };
        }

        /// <summary>
        /// Converts a Tyhp diagnostic span to an LSP range. When no end is present,
        /// the range is zero-width at the start.
        /// </summary>
        public static ProtocolRange ToLspRange(IDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Position start = ToLspPosition(diagnostic.Line, diagnostic.Column);
            int endLine = diagnostic.EndLine ?? diagnostic.Line;
            int endColumn = diagnostic.EndColumn ?? diagnostic.Column;
            Position end = ToLspPosition(endLine, endColumn);
            if (end.Line < start.Line
                || (end.Line == start.Line && end.Character < start.Character))
            {
                end = start;
            }

            return new ProtocolRange { Start = start, End = end };
        }

        /// <summary>
        /// Converts an LSP position to a UTF-16 offset in <paramref name="text"/>.
        /// Positions past the end of a line or the document are clamped.
        /// </summary>
        public static int GetOffset(string text, Position position)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(position);

            int targetLine = Math.Max(0, position.Line);
            int targetCharacter = Math.Max(0, position.Character);
            int index = 0;
            int currentLine = 0;
            int length = text.Length;

            while (index < length && currentLine < targetLine)
            {
                char c = text[index++];
                if (c == '\r')
                {
                    if (index < length && text[index] == '\n')
                    {
                        index++;
                    }

                    currentLine++;
                }
                else if (c == '\n')
                {
                    currentLine++;
                }
            }

            int lineEnd = index;
            while (lineEnd < length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
            {
                lineEnd++;
            }

            int target = index + targetCharacter;
            return target > lineEnd ? lineEnd : target;
        }

        /// <summary>
        /// Converts a UTF-16 character offset to an LSP position. Offsets past the
        /// end of the document are clamped. Empty text and EOF map to (0, 0) / last line.
        /// </summary>
        public static Position GetPosition(string text, int offset)
        {
            ArgumentNullException.ThrowIfNull(text);
            int length = text.Length;
            offset = Math.Clamp(offset, 0, length);

            int line = 0;
            int lineStart = 0;
            int index = 0;
            while (index < offset)
            {
                char c = text[index];
                if (c == '\r')
                {
                    index++;
                    if (index < length && text[index] == '\n')
                    {
                        index++;
                    }

                    line++;
                    lineStart = index;
                    continue;
                }

                if (c == '\n')
                {
                    index++;
                    line++;
                    lineStart = index;
                    continue;
                }

                index++;
            }

            return new Position
            {
                Line = line,
                Character = Math.Max(0, offset - lineStart),
            };
        }

        /// <summary>
        /// Range covering just the identifier <paramref name="identifier"/> inside
        /// <paramref name="node"/>'s source span. Declaration nodes often span a whole
        /// body; this clips to the name token so rename/find-references do not select
        /// the entire construct. When the name cannot be located, falls back to
        /// <see cref="ToLspRange(IBase2Ast)"/>.
        /// </summary>
        public static ProtocolRange ToIdentifierRange(IBase2Ast node, string identifier, string? text)
        {
            ArgumentNullException.ThrowIfNull(node);
            ProtocolRange span = ToLspRange(node);
            if (string.IsNullOrEmpty(identifier))
            {
                return span;
            }

            string needle = identifier;
            string bare = identifier.StartsWith('$') ? identifier[1..] : identifier;
            if (string.IsNullOrEmpty(text))
            {
                return ShrinkToNameLength(span, needle);
            }

            int start = GetOffset(text, span.Start);
            int end = GetOffset(text, span.End);
            if (end < start)
            {
                end = start;
            }

            end = Math.Min(end, text.Length);
            start = Math.Clamp(start, 0, text.Length);
            if (start >= end)
            {
                return span;
            }

            string slice = text[start..end];
            if (ShouldClipToHeader(node))
            {
                int cut = FirstIndexOfAny(slice, ['(', '{', ';']);
                if (cut > 0)
                {
                    slice = slice[..cut];
                }
            }

            if (TryFindIdentifier(slice, needle, out int found, out string matched)
                || TryFindIdentifier(slice, bare, out found, out matched)
                || TryFindIdentifier(slice, "$" + bare, out found, out matched))
            {
                int abs = start + found;
                return new ProtocolRange
                {
                    Start = GetPosition(text, abs),
                    End = GetPosition(text, abs + matched.Length),
                };
            }

            return ShrinkToNameLength(span, needle);
        }

        private static bool ShouldClipToHeader(IBase2Ast node)
        {
            return node is Tyhp.TyhpLang.Ast.PhpFunctionDeclAst
                or Tyhp.TyhpLang.Ast.PhpMethodDeclAst
                or Tyhp.TyhpLang.Ast.PhpObjectTypeDeclAst
                or Tyhp.TyhpLang.Ast.PhpConstDeclAst
                or Tyhp.TyhpLang.Ast.PhpParameterAst
                or Tyhp.TyhpLang.Ast.PhpPropertyAst
                or Tyhp.TyhpLang.Ast.PhpPropertyDeclAst;
        }

        private static bool TryFindIdentifier(string slice, string needle, out int index, out string matched)
        {
            index = -1;
            matched = needle;
            if (string.IsNullOrEmpty(needle) || slice.Length < needle.Length)
            {
                return false;
            }

            int from = slice.Length;
            while (from >= needle.Length)
            {
                int hit = slice.LastIndexOf(needle, from - 1, StringComparison.Ordinal);
                if (hit < 0)
                {
                    return false;
                }

                bool startOk = hit == 0 || !IdentifierSyntax.IsIdentifierChar(slice[hit - 1]);
                int after = hit + needle.Length;
                bool endOk = after >= slice.Length || !IdentifierSyntax.IsIdentifierChar(slice[after]);
                if (startOk && endOk)
                {
                    index = hit;
                    return true;
                }

                from = hit;
            }

            return false;
        }

        private static int FirstIndexOfAny(string text, char[] chars)
        {
            int best = -1;
            foreach (char c in chars)
            {
                int hit = text.IndexOf(c);
                if (hit >= 0 && (best < 0 || hit < best))
                {
                    best = hit;
                }
            }

            return best;
        }

        private static ProtocolRange ShrinkToNameLength(ProtocolRange span, string identifier)
        {
            if (string.IsNullOrEmpty(identifier)
                || span.Start is null
                || span.End is null
                || span.Start.Line != span.End.Line)
            {
                return span;
            }

            int width = span.End.Character - span.Start.Character;
            if (width <= identifier.Length)
            {
                return span;
            }

            return new ProtocolRange
            {
                Start = span.Start,
                End = new Position
                {
                    Line = span.Start.Line,
                    Character = span.Start.Character + identifier.Length,
                },
            };
        }

        /// <summary>
        /// Resolves a node's exclusive end position, estimating from children when
        /// <see cref="IBase2Ast.EndLine"/> is unset.
        /// </summary>
        internal static (int EndLine, int EndColumn) ResolveEnd(IBase2Ast node)
        {
            if (node.EndLine >= 1 && node.EndColumn >= 0)
            {
                return (node.EndLine, node.EndColumn);
            }

            int bestLine = -1;
            int bestColumn = -1;
            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is null)
                {
                    continue;
                }

                var (childEndLine, childEndColumn) = ResolveEnd(child);
                if (childEndLine < 1)
                {
                    continue;
                }

                if (childEndLine > bestLine
                    || (childEndLine == bestLine && childEndColumn > bestColumn))
                {
                    bestLine = childEndLine;
                    bestColumn = childEndColumn;
                }
            }

            return (bestLine, bestColumn);
        }
    }
}
