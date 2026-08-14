using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public abstract class BaseSymbol :
        Interfaces.IBaseSymbol
    {
        /// <summary>
        /// The declared name for this symbol.
        /// </summary>
        public string Name { get; protected internal set; }

        /// <summary>
        /// Fully qualified symbol name with namespace prefix.
        /// </summary>
        public string FullyQualifiedName { get; protected internal set; }

        /// <summary>
        /// The AST node that declared this symbol.
        /// </summary>
        public IBase2Ast? DeclaringAstNode { get; protected internal set; }

        /// <summary>
        /// The scope this symbol belongs to.
        /// </summary>
        public IBaseScope? ContainingScope { get; protected internal set; }

        /// <summary>
        /// The kind of this symbol.
        /// </summary>
        public SymbolType SymbolType { get; internal set; }

        /// <summary>
        /// Indicates whether this symbol has a declared identifier that participates in
        /// name-based identity and duplicate checks.
        /// </summary>
        public bool HasDeclaredName => !string.IsNullOrWhiteSpace(this.Name);

        /// <summary>
        /// Modifiers/visibility attached to this declaration.
        /// </summary>
        public MemberModifier Visibility { get; protected internal set; }

        /// <summary>
        /// Whether this symbol is marked deprecated.
        /// </summary>
        public bool IsDeprecated { get; protected internal set; }

        /// <summary>
        /// Whether this symbol is marked obsolete.
        /// </summary>
        public bool IsObsolete { get; protected internal set; }

        /// <summary>
        /// Documentation comment associated with the declaration.
        /// </summary>
        public string? DocComment { get; protected internal set; }

        /// <summary>
        /// Source file where this symbol was declared.
        /// </summary>
        public string SourceFile { get; protected internal set; }

        /// <summary>
        /// Source line where this symbol was declared.
        /// </summary>
        public int Line { get; protected internal set; }

        /// <summary>
        /// Source column where this symbol was declared.
        /// </summary>
        public int Column { get; protected internal set; }

        /// <summary>
        /// Initializes the base symbol data that all symbols share.
        /// </summary>
        /// <param name="name">Declared symbol name.</param>
        /// <param name="symbolType">Symbol discriminator.</param>
        /// <param name="declaringNode">Optional AST node.</param>
        /// <param name="sourceFile">Source filename.</param>
        /// <param name="visibility">Symbol visibility / modifiers.</param>
        protected BaseSymbol(
            string name,
            SymbolType symbolType,
            IBase2Ast? declaringNode = null,
            string sourceFile = "",
            MemberModifier visibility = MemberModifier.None
        )
        {
            ArgumentNullException.ThrowIfNull(name);
            this.Name = name;
            this.SymbolType = symbolType;
            this.DeclaringAstNode = declaringNode;
            this.SourceFile = sourceFile;
            this.Visibility = visibility;
            this.IsDeprecated = false;
            this.IsObsolete = false;
            this.DocComment = declaringNode?.DocComment;
            this.Line = declaringNode?.Line ?? 0;
            this.Column = declaringNode?.Column ?? 0;
            this.FullyQualifiedName = name;

            if (declaringNode != null)
            {
                declaringNode.BoundSymbol = this;
            }
        }
    }
}