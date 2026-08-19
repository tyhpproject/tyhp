namespace Tyhp.LanguageServer.Analysis
{
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;

    /// <summary>
    /// One occurrence of a symbol (declaration or usage) in a project AST.
    /// </summary>
    public sealed class SymbolReference
    {
        public SymbolReference(
            IBase2Ast node,
            SrcFileAst file,
            SymbolReferenceKind kind,
            bool isDeclaration)
        {
            this.Node = node ?? throw new ArgumentNullException(nameof(node));
            this.File = file ?? throw new ArgumentNullException(nameof(file));
            this.Kind = kind;
            this.IsDeclaration = isDeclaration;
        }

        /// <summary>AST node covering the occurrence (preferably the identifier token).</summary>
        public IBase2Ast Node { get; }

        /// <summary>File AST that contains <see cref="Node"/>.</summary>
        public SrcFileAst File { get; }

        /// <summary>Read, write, or plain-text (declaration without initializer).</summary>
        public SymbolReferenceKind Kind { get; }

        /// <summary>True when this occurrence is the symbol's declaration.</summary>
        public bool IsDeclaration { get; }
    }

    /// <summary>
    /// How a symbol occurrence is used. Maps onto LSP <c>DocumentHighlightKind</c>.
    /// </summary>
    public enum SymbolReferenceKind
    {
        Text = 1,
        Read = 2,
        Write = 3,
    }
}
