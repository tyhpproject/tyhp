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

        public FunctionDeclarationSymbol(string name, IBase2Ast? declaringNode = null, string sourceFile = "", MemberModifier visibility = MemberModifier.None)
            : base(name, SymbolType.FunctionDeclaration, declaringNode, sourceFile, visibility)
        {
        }
    }
}