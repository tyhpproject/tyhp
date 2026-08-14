using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Extension methods for creating diagnostics from AST nodes.
    /// Automatically extracts line and column information from AST nodes to simplify diagnostic reporting.
    /// </summary>
    public static class DiagnosticExtensions
    {
        /// <summary>
        /// Returns the node's exclusive end span for diagnostics when both end positions are known
        /// (<c>&gt;= 0</c>); otherwise both outs are <see langword="null"/> so renderers keep a
        /// single-caret fallback.
        /// </summary>
        public static void GetOptionalEnd(IBase2Ast node, out int? endLine, out int? endColumn)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.EndLine >= 0 && node.EndColumn >= 0)
            {
                endLine = node.EndLine;
                endColumn = node.EndColumn;
                return;
            }

            endLine = null;
            endColumn = null;
        }

        /// <summary>
        /// Adds a diagnostic to the bag using position information from an AST node.
        /// </summary>
        public static void AddFromAst(
            this DiagnosticBag bag,
            DiagnosticSeverity severity,
            MessageCode code,
            IBase2Ast node,
            string fileName,
            params object[] formatParams)
        {
            GetOptionalEnd(node, out var endLine, out var endColumn);
            var diagnostic = severity switch
            {
                DiagnosticSeverity.Error => Diagnostic.Error(
                    code, fileName, node.Line, node.Column, formatParams, endLine, endColumn),
                DiagnosticSeverity.Warning => Diagnostic.Warning(
                    code, fileName, node.Line, node.Column, formatParams, endLine, endColumn),
                DiagnosticSeverity.Info => Diagnostic.Info(
                    code, fileName, node.Line, node.Column, formatParams, endLine, endColumn),
                DiagnosticSeverity.Hint => Diagnostic.Hint(
                    code, fileName, node.Line, node.Column, formatParams, endLine, endColumn),
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown diagnostic severity")
            };

            bag.Add(diagnostic);
        }

        /// <summary>
        /// Adds an error diagnostic to the bag using position information from an AST node.
        /// </summary>
        public static void AddErrorFromAst(
            this DiagnosticBag bag,
            MessageCode code,
            IBase2Ast node,
            string fileName,
            params object[] formatParams)
        {
            bag.AddFromAst(DiagnosticSeverity.Error, code, node, fileName, formatParams);
        }

        /// <summary>
        /// Adds a warning diagnostic to the bag using position information from an AST node.
        /// </summary>
        public static void AddWarningFromAst(
            this DiagnosticBag bag,
            MessageCode code,
            IBase2Ast node,
            string fileName,
            params object[] formatParams)
        {
            bag.AddFromAst(DiagnosticSeverity.Warning, code, node, fileName, formatParams);
        }

        /// <summary>
        /// Creates a labeled secondary span from an AST node's start/end positions.
        /// Explicit <paramref name="endLine"/> / <paramref name="endColumn"/> override the node.
        /// </summary>
        public static DiagnosticLabel LabelFromAst(
            IBase2Ast node,
            string fileName,
            string label,
            int? endLine = null,
            int? endColumn = null)
            => DiagnosticLabel.Create(
                DiagnosticSpan.FromAst(node, fileName, endLine, endColumn),
                label);

        /// <summary>
        /// Returns the primary span for any diagnostic.
        /// </summary>
        public static DiagnosticSpan GetPrimarySpan(this IDiagnostic diagnostic)
            => DiagnosticSpan.FromDiagnostic(diagnostic);
    }
}
