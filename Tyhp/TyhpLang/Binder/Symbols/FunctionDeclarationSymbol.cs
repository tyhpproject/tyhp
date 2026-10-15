using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class FunctionDeclarationSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol
    {
        // Lazy initialization via ??= is not thread-safe; the binder is assumed to be single-threaded.
        private List<ParameterInfo>? _parameters;
        private List<GenericTypeParameterSymbol>? _genericParameters;

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

        /// <summary>
        /// Tyhpdef <c>fallback function</c>. The binder keeps one package's signatures
        /// for the name, chosen by Composer <c>autoload.files</c> order.
        /// </summary>
        public bool IsFallback { get; internal set; }

        /// <summary>
        /// When set, this tyhpdef free-function was declared as
        /// <c>function php_name as tyhpName(...)</c>. The symbol is registered under
        /// <see cref="BaseSymbol.Name"/> (the Tyhp-facing alias); emit erases calls to
        /// <see cref="OriginalPhpName"/> (see <c>EmitContext.TyhpdefAliasMap</c>).
        /// Matches <see cref="ObjectMethodSymbol.OriginalPhpName"/> for member aliases.
        /// </summary>
        public string? OriginalPhpName { get; internal set; }

        /// <summary>
        /// Additional overload signatures for tyhpdef-declared functions with the same name.
        /// </summary>
        public List<FunctionDeclarationSymbol> Overloads { get; } = new();

        /// <summary>
        /// Role inside a <c>.tyhp</c> overload set. <see cref="SourceOverloadRole.Implementation"/>
        /// hides this symbol's own signature from callers; <see cref="SourceOverloadRole.Signature"/>
        /// marks an overload signature, which is compile-time only (erased at emit).
        /// </summary>
        public SourceOverloadRole OverloadRole { get; internal set; }

        /// <summary>For a <see cref="SourceOverloadRole.Signature"/>, the registered declaration of the set.</summary>
        public FunctionDeclarationSymbol? OverloadOwner { get; internal set; }

        /// <summary>True for a bodyless <c>.tyhp</c> overload signature.</summary>
        public bool IsOverloadSignature => this.OverloadRole == SourceOverloadRole.Signature;

        /// <summary>True when this symbol heads a <c>.tyhp</c> overload set.</summary>
        public bool IsSourceOverloadSet => this.OverloadRole == SourceOverloadRole.Implementation;

        /// <summary>The symbol that is emitted for this one: the set's registered declaration, or itself.</summary>
        public FunctionDeclarationSymbol EmittedSymbol => this.OverloadOwner ?? this;

        public FunctionDeclarationSymbol(string name, IBase2Ast? declaringNode = null, string sourceFile = "", MemberModifier visibility = MemberModifier.None)
            : base(name, SymbolType.FunctionDeclaration, declaringNode, sourceFile, visibility)
        {
        }
    }
}