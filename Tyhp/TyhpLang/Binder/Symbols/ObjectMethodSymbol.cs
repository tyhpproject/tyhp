using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectMethodSymbol :
        BaseSymbol,
        IInstanceMethodDeclarationSymbol,
        IStaticMethodDeclarationSymbol,
        IObjectDeclarationScopeSymbol
    {
        // Lazy initialization via ??= is not thread-safe; the binder is assumed to be single-threaded.
        private List<ParameterInfo>? _parameters;
        private List<GenericTypeParameterSymbol>? _genericParameters;
        // Design decision: keeping granular magic-method symbol classes (ObjectMagicCallMethodSymbol, etc.)
        // rather than consolidating into a single ObjectMethodSymbol with a MagicMethodKind discriminator.
        // This preserves type-safe dispatch per magic method and avoids runtime kind checks.
        // ObjectMethodSymbol provides shared method metadata for all variants.
        public List<ParameterInfo> Parameters
        {
            get => this._parameters ??= new List<ParameterInfo>();
            internal set => this._parameters = value;
        }

        public ITypeExpression? ReturnType { get; internal set; }

        public List<GenericTypeParameterSymbol> GenericParameters
        {
            get => this._genericParameters ??= new List<GenericTypeParameterSymbol>();
            internal set => this._genericParameters = value;
        }

        public bool IsGenerator { get; internal set; }

        public bool IsAsync { get; internal set; }

        public bool IsAbstract { get; internal set; }

        public bool IsStatic { get; internal set; }

        /// <summary>
        /// When set, this method was imported from a tyhpdef under an <c>as</c> alias: <see cref="BaseSymbol.Name"/>
        /// is the Tyhp-facing name and this is the original PHP method name to emit.
        /// </summary>
        public string? OriginalPhpName { get; internal set; }

        /// <summary>
        /// Additional overload signatures for the same method name. Overlay last-wins
        /// replace keeps the first overlay declaration as this symbol; later same-name methods
        /// in that overlay body append here. Tyhp source extension overload signatures
        /// (bodyless <c>function name(...): T;</c> plus an implementation) are attached the
        /// same way. Same pattern as <see cref="FunctionDeclarationSymbol.Overloads"/>.
        /// </summary>
        public List<ObjectMethodSymbol> Overloads { get; } = new();

        /// <summary>
        /// Role inside a <c>.tyhp</c> overload set. <see cref="SourceOverloadRole.Implementation"/>
        /// hides this symbol's own signature from callers; <see cref="SourceOverloadRole.Signature"/>
        /// marks an overload signature, which is compile-time only (erased at emit).
        /// </summary>
        public SourceOverloadRole OverloadRole { get; internal set; }

        /// <summary>For a <see cref="SourceOverloadRole.Signature"/>, the registered declaration of the set.</summary>
        public ObjectMethodSymbol? OverloadOwner { get; internal set; }

        /// <summary>True for a bodyless <c>.tyhp</c> overload signature.</summary>
        public bool IsOverloadSignature => this.OverloadRole == SourceOverloadRole.Signature;

        /// <summary>True when this symbol heads a <c>.tyhp</c> overload set.</summary>
        public bool IsSourceOverloadSet =>
            this.OverloadRole is SourceOverloadRole.Implementation or SourceOverloadRole.LastSignature;

        /// <summary>The symbol that is emitted for this one: the set's registered declaration, or itself.</summary>
        public ObjectMethodSymbol EmittedSymbol => this.OverloadOwner ?? this;

        public virtual bool CanBeStatic => true;
        public virtual bool CanBeInstance => true;

        public ObjectMethodSymbol(
            string name,
            IBase2Ast? declaringNode = null,
            string sourceFile = "",
            MemberModifier visibility = MemberModifier.None,
            SymbolType symbolType = SymbolType.InstanceObjectMethod
        )
            : base(name, symbolType, declaringNode, sourceFile, visibility)
        {
            this.IsStatic = symbolType == SymbolType.StaticObjectMethod
                || symbolType == SymbolType.ObjectMagicCallStaticMethod
                || symbolType == SymbolType.ObjectMagicSetStateMethod
                || symbolType == SymbolType.ObjectOperatorOverload
                || symbolType == SymbolType.StaticObjectAccessorMethod;
        }
    }
}