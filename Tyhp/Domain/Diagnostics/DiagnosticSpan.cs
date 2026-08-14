using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// A source location span using Tyhp's internal coordinate contract:
    /// 1-based <see cref="Line"/> / 0-based <see cref="Column"/> (matching
    /// <see cref="IBase2Ast.Line"/> / <see cref="IBase2Ast.Column"/>), with optional
    /// end positions matching <see cref="IDiagnostic.EndLine"/> / <see cref="IDiagnostic.EndColumn"/>.
    /// </summary>
    public readonly record struct DiagnosticSpan(
        string FileName,
        int Line,
        int Column,
        int? EndLine = null,
        int? EndColumn = null)
    {
        /// <summary>
        /// Creates a span from an AST node's start position. When <paramref name="endLine"/> /
        /// <paramref name="endColumn"/> are omitted, uses the node's exclusive end positions when
        /// both are known (<c>&gt;= 0</c>).
        /// </summary>
        public static DiagnosticSpan FromAst(
            IBase2Ast node,
            string fileName,
            int? endLine = null,
            int? endColumn = null)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (!endLine.HasValue || !endColumn.HasValue)
            {
                DiagnosticExtensions.GetOptionalEnd(node, out var nodeEndLine, out var nodeEndColumn);
                endLine ??= nodeEndLine;
                endColumn ??= nodeEndColumn;
            }

            return new DiagnosticSpan(
                fileName ?? string.Empty,
                Math.Max(1, node.Line),
                Math.Max(0, node.Column),
                endLine.HasValue ? Math.Max(1, endLine.Value) : null,
                endColumn.HasValue ? Math.Max(0, endColumn.Value) : null);
        }

        /// <summary>
        /// Creates a span from a diagnostic's primary location fields.
        /// </summary>
        public static DiagnosticSpan FromDiagnostic(IDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return new DiagnosticSpan(
                diagnostic.FileName ?? string.Empty,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.EndLine,
                diagnostic.EndColumn);
        }

        /// <summary>
        /// Effective end line (falls back to <see cref="Line"/> when unspecified).
        /// </summary>
        public int EffectiveEndLine => this.EndLine ?? this.Line;

        /// <summary>
        /// Effective end column (falls back to <see cref="Column"/> when unspecified).
        /// </summary>
        public int EffectiveEndColumn => this.EndColumn ?? this.Column;
    }
}
