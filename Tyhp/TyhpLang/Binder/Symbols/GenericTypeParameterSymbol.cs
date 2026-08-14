using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class GenericTypeParameterSymbol :
        BaseSymbol,
        IFunctionDeclarationScopeSymbol,
        IAnonymousFunctionScopeSymbol,
        IObjectDeclarationScopeSymbol,
        IInstanceMethodDeclarationScopeSymbol,
        IStaticMethodDeclarationScopeSymbol
    {
        public ITypeExpression? Constraint { get; internal set; }

        /// <summary>
        /// Checked form of <see cref="Constraint"/>, populated by the checker when the declaring
        /// generic scope is entered. Used as the type parameter's upper bound for assignability
        /// (e.g. <c>T extends object</c> is assignable to <c>object</c>) and for derived facts such
        /// as foreach key types over <c>TProperties extends struct</c>.
        /// </summary>
        public ICheckedType? ResolvedConstraint { get; set; }

        public TypeVariance Variance { get; internal set; }

        public ITypeExpression? DefaultType { get; internal set; }

        /// <summary>
        /// Whether this generic parameter declares a default type.
        /// Convenience property equivalent to <c>DefaultType != null</c>.
        /// </summary>
        public bool HasDefault => this.DefaultType != null;

        public GenericTypeParameterSymbol(
            string name,
            SymbolType symbolType,
            IBase2Ast? declaringNode = null,
            string sourceFile = "",
            MemberModifier visibility = MemberModifier.None
        )
            : base(name, symbolType, declaringNode, sourceFile, visibility)
        {
        }
    }
}