namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// LSP <c>textDocument/selectionRange</c> request. The 17.2.8 protocol package
    /// does not include this 3.15 method, so the DTOs live with the collector.
    /// </summary>
    public sealed class SelectionRangeParams
    {
        public TextDocumentIdentifier? TextDocument { get; set; }

        public Position[]? Positions { get; set; }
    }

    /// <summary>
    /// Linked selection range: <see cref="Parent"/> is the next larger range.
    /// </summary>
    public sealed class SelectionRange
    {
        public ProtocolRange? Range { get; set; }

        public SelectionRange? Parent { get; set; }
    }

    /// <summary>
    /// Builds nested selection ranges by walking the AST parent chain.
    /// </summary>
    internal static class SelectionRangeCollector
    {
        /// <summary>
        /// Selection-range chain for each requested position: token → expression →
        /// statement → block → function → class → file.
        /// </summary>
        public static SelectionRange[] Collect(
            SrcFileAst ast,
            string content,
            Position[] positions,
            SymbolFinder finder)
        {
            ArgumentNullException.ThrowIfNull(ast);
            ArgumentNullException.ThrowIfNull(finder);
            content ??= string.Empty;
            if (positions is null || positions.Length == 0)
            {
                return [];
            }

            ProtocolRange fileRange = WholeFileRange(content);
            var results = new SelectionRange[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                results[i] = CollectOne(ast, content, positions[i], finder, fileRange);
            }

            return results;
        }

        private static SelectionRange CollectOne(
            SrcFileAst ast,
            string content,
            Position position,
            SymbolFinder finder,
            ProtocolRange fileRange)
        {
            var (line, column) = PositionUtilities.FromLspPosition(position);
            IReadOnlyList<IBase2Ast>? path = finder.FindPathAtPosition(ast, line, column);
            var ranges = new List<ProtocolRange>();
            if (path is { Count: > 0 })
            {
                IBase2Ast leaf = path[^1];
                string name = SymbolFinder.GetDisplayName(leaf);
                ProtocolRange leafRange = string.IsNullOrEmpty(name)
                    ? PositionUtilities.ToLspRange(leaf)
                    : PositionUtilities.ToIdentifierRange(leaf, name, content);
                AddExpanding(ranges, leafRange);

                for (int i = path.Count - 1; i >= 0; i--)
                {
                    IBase2Ast node = path[i];
                    if (!IsSelectionNode(node))
                    {
                        continue;
                    }

                    ProtocolRange span = PositionUtilities.ToLspRange(node);
                    AddExpanding(ranges, span);
                }
            }

            AddExpanding(ranges, fileRange);
            return Link(ranges);
        }

        private static bool IsSelectionNode(IBase2Ast node)
        {
            if (node is SrcFileAst or PhpTopStatementListAst or PhpImportDeclListAst)
            {
                return false;
            }

            if (node is TokenValueAst and not PhpNameAst and not PhpMagicConstantAst)
            {
                return false;
            }

            return node is PhpNameAst
                or PhpVariableAst
                or PhpScalarAst
                or PhpMagicConstantAst
                or PhpBinaryOpAst
                or PhpTernaryOpAst
                or PhpCallAst
                or PhpMemberAccessAst
                or PhpInstanceMemberAccessAst
                or PhpStaticMemberAccessAst
                or PhpNewAst
                or PhpArrayAst
                or PhpYieldAst
                or PhpReturnStatementAst
                or PhpIfAst
                or PhpLoopAst
                or PhpJumpStatementAst
                or PhpEchoStatementAst
                or PhpTryCatchAst
                or PhpCatchClauseAst
                or PhpConditionalAst
                or PhpStatementBlockAst
                or PhpClassBodyAst
                or PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpInlineFunctionAst
                or TyhpAsyncBlockAst
                or PhpObjectTypeDeclAst
                or TyhpStructDeclAst
                or TyhpExtensionDeclAst
                or PhpBlockNamespaceDeclAst
                or PhpNamespaceDeclAst
                or PhpNamedTypeAst
                or ITypeExpression;
        }

        private static void AddExpanding(List<ProtocolRange> ranges, ProtocolRange candidate)
        {
            if (candidate.Start is null || candidate.End is null)
            {
                return;
            }

            if (ranges.Count == 0)
            {
                ranges.Add(candidate);
                return;
            }

            ProtocolRange last = ranges[^1];
            if (SameRange(last, candidate))
            {
                return;
            }

            if (!Contains(candidate, last))
            {
                return;
            }

            ranges.Add(candidate);
        }

        private static SelectionRange Link(List<ProtocolRange> ranges)
        {
            SelectionRange? current = null;
            for (int i = ranges.Count - 1; i >= 0; i--)
            {
                current = new SelectionRange
                {
                    Range = ranges[i],
                    Parent = current,
                };
            }

            return current ?? new SelectionRange { Range = WholeFileRange(string.Empty) };
        }

        private static ProtocolRange WholeFileRange(string content)
        {
            return new ProtocolRange
            {
                Start = new Position { Line = 0, Character = 0 },
                End = PositionUtilities.GetPosition(content, content.Length),
            };
        }

        private static bool SameRange(ProtocolRange a, ProtocolRange b)
            => a.Start is not null
            && a.End is not null
            && b.Start is not null
            && b.End is not null
            && a.Start.Line == b.Start.Line
            && a.Start.Character == b.Start.Character
            && a.End.Line == b.End.Line
            && a.End.Character == b.End.Character;

        private static bool Contains(ProtocolRange outer, ProtocolRange inner)
        {
            if (outer.Start is null || outer.End is null || inner.Start is null || inner.End is null)
            {
                return false;
            }

            return Compare(outer.Start, inner.Start) <= 0 && Compare(inner.End, outer.End) <= 0;
        }

        private static int Compare(Position a, Position b)
        {
            int line = a.Line.CompareTo(b.Line);
            return line != 0 ? line : a.Character.CompareTo(b.Character);
        }
    }
}
