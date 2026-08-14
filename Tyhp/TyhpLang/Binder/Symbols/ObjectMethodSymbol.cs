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