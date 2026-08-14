using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Ast.Interfaces
{
    public interface IBase2Ast
    {
        string? LanguageMode { get; }
        int Line { get; }
        int Column { get; }
        int StartIndex { get; }

        /// <summary>
        /// Ending line of the node (1-indexed), or <c>-1</c> when unknown.
        /// </summary>
        int EndLine { get; }

        /// <summary>
        /// Exclusive ending column on <see cref="EndLine"/> (0-indexed, one past the last
        /// character of the ANTLR stop token), or <c>-1</c> when unknown. Matches
        /// <c>IDiagnostic.EndColumn</c> so rich underlines can span the full node.
        /// </summary>
        int EndColumn { get; }

        /// <summary>
        /// Inclusive character index of the last character of the node, or <c>-1</c> when unknown.
        /// </summary>
        int EndIndex { get; }
        string? DocComment { get; }
        string? ValueString { get; }
        long? ValueInt64 { get; }
        decimal? ValueDecimal { get; }
        bool? ValueBoolean { get; }
        IReadOnlyList<IBase2Ast?> AstChildren { get; }
        IReadOnlyList<IBase2Ast> AstAttributes { get; }
        IReadOnlyDictionary<string, IBase2Ast> AstGrammarAddons { get; }
        string Identifier { get; }

        /// <summary>
        /// The symbol bound to this AST node by the binder.
        /// For declaration nodes: the symbol this node declares.
        /// For reference nodes: the symbol this reference resolves to.
        /// Null if no symbol is associated (e.g., literals, operators).
        /// Set only by the binder; read-only for all subsequent phases.
        /// </summary>
        IBaseSymbol? BoundSymbol { get; set; }

        /// <summary>
        /// The source file AST that owns this node. Set during binding.
        /// Enables downstream phases to navigate from any AST node to its
        /// owning source file without parent-chain traversal.
        /// </summary>
        SrcFileAst? OwningFile { get; set; }

        void AddAttributes(PhpAttributeListAst? attributes);
        void AddGrammarAddon(string key, IBase2Ast? addon);
        bool IsValid();
        byte[] Serialize();
    }
}