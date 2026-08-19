namespace Tyhp.LanguageServer.Analysis
{
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;

    /// <summary>
    /// Result of resolving the symbol (or local declaration) under an LSP cursor.
    /// </summary>
    public sealed class SymbolLookupResult
    {
        public SymbolLookupResult(
            IBase2Ast node,
            BaseSymbol? symbol,
            IBase2Ast? declaringNode,
            string? sourceFile,
            IBase2Ast? inferredTypeNode = null)
        {
            this.Node = node ?? throw new ArgumentNullException(nameof(node));
            this.Symbol = symbol;
            this.DeclaringNode = declaringNode ?? symbol?.DeclaringAstNode;
            this.SourceFile = sourceFile ?? symbol?.SourceFile;
            this.InferredTypeNode = inferredTypeNode ?? node;
        }

        /// <summary>Deepest AST node containing the cursor.</summary>
        public IBase2Ast Node { get; }

        /// <summary>Bound or resolved symbol, when one exists.</summary>
        public BaseSymbol? Symbol { get; }

        /// <summary>AST node to navigate to for go-to-definition.</summary>
        public IBase2Ast? DeclaringNode { get; }

        /// <summary>Declaring source file path (may be a <c>.tyhpdef</c>).</summary>
        public string? SourceFile { get; }

        /// <summary>
        /// Node whose checker-inferred type should back a hover with no declared type.
        /// Usually the same as <see cref="Node"/>, except when the cursor is on the left
        /// side of a plain <c>=</c> assignment: the checker never calls
        /// <c>InferExpressionType</c> on the assignment target, so its own dictionary entry
        /// (if any, from an earlier read of the same node) is stale or unresolved. In that
        /// case this points at the assignment's right-hand side, which the checker always
        /// infers.
        /// </summary>
        public IBase2Ast InferredTypeNode { get; }
    }
}
