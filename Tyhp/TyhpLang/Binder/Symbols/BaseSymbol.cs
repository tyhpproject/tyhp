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
        /// Whether this symbol is marked deprecated (tyhpdef <c>deprecated</c> keyword and/or
        /// <c>#[\Deprecated]</c> on the declaration).
        /// </summary>
        public bool IsDeprecated { get; protected internal set; }

        /// <summary>
        /// Literal <c>$message</c> from <c>#[\Deprecated]</c> when that argument is a string
        /// literal. Null when absent or not a literal. Used by TYHP4500 as <c>{1}</c>.
        /// </summary>
        public string? DeprecatedMessage { get; protected internal set; }

        /// <summary>
        /// Whether this symbol is marked obsolete.
        /// </summary>
        public bool IsObsolete { get; protected internal set; }

        /// <summary>
        /// Whether this symbol was declared with Tyhp <c>internal</c>.
        /// Internal symbols compile to public / unprefixed PHP. Public compiled-library
        /// <c>package.tyhpdef</c> must omit them the same way <c>private</c> is skipped
        /// (see <see cref="SymbolExportVisibility"/>).
        /// </summary>
        public bool IsInternal { get; set; }

        /// <summary>
        /// Tyhpdef name-only <c>extern</c> placeholder (type, function, or const).
        /// A bound extern name does not produce unresolved-name diagnostics. Real
        /// declarations of the same FQCN and kind replace the placeholder.
        /// </summary>
        public bool IsExtern { get; internal set; }

        /// <summary>
        /// Optional <c>@provided-by</c> wrapper package name from the comment immediately
        /// preceding the <c>extern</c> declaration (e.g. <c>tyhpdef/php-ext-bcmath</c>).
        /// </summary>
        public string? ProvidedBy { get; internal set; }

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
        /// Effective AND-stack of PHP version constraints that gated this declaration
        /// (<c>declare(php=…)</c> plus <c>#[\Tyhp\Php]</c>). Empty means unrestricted.
        /// </summary>
        public IReadOnlyList<string> EffectivePhpVersionConstraints { get; internal set; } = [];

        /// <summary>
        /// Enclosing <c>declare(ext="…")</c> specs (<c>intl</c> or <c>!intl</c>) that gated this
        /// declaration, outermost first. Empty means no extension gate.
        /// </summary>
        public IReadOnlyList<string> EffectiveExtGates { get; internal set; } = [];

        /// <summary>
        /// True when the same name is also declared under a php gate that is not satisfied at the
        /// compile target but could hold on a newer PHP. That declaration is not compiled, so this
        /// one is emitted without a <c>\PHP_VERSION_ID</c> check.
        /// </summary>
        public bool HasUncompiledVersionVariants { get; internal set; }

        /// <summary>
        /// Bound <c>#[\Tyhp\GenericRuntime]</c> helper names for consumer emit. Null when the
        /// declaration is check-only (erase type arguments / inline alias bodies).
        /// </summary>
        public GenericRuntimeInfo? GenericRuntime { get; internal set; }

        /// <summary>
        /// Ending source line of the declaring AST node, or <c>0</c> when no declaring node
        /// was provided. <c>-1</c> means the AST node itself has no end position.
        /// </summary>
        public int EndLine { get; protected internal set; }

        /// <summary>
        /// Exclusive ending column of the declaring AST node, or <c>0</c> when no declaring
        /// node was provided. Matches <see cref="IBase2Ast.EndColumn"/>.
        /// </summary>
        public int EndColumn { get; protected internal set; }

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
            EngineDeprecatedAttribute.Apply(this);
            this.DocComment = declaringNode?.DocComment;
            this.Line = declaringNode?.Line ?? 0;
            this.Column = declaringNode?.Column ?? 0;
            this.EndLine = declaringNode?.EndLine ?? 0;
            this.EndColumn = declaringNode?.EndColumn ?? 0;
            this.FullyQualifiedName = name;

            if (declaringNode != null)
            {
                declaringNode.BoundSymbol = this;
            }
        }
    }
}