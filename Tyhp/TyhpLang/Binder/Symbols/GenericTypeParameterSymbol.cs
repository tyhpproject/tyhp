using Tyhp.TyhpLang.Ast;
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

        /// <summary>
        /// True when the declared bound is <c>__Awaited&lt;X&gt;</c> (<c>T extends __Awaited&lt;void|mixed&gt;</c>):
        /// the parameter only ever holds an already-awaited type, so it can never be (or contain) a
        /// <c>\Tyhp\Promise</c>. <see cref="ResolvedConstraint"/> still holds the settled payload
        /// <c>X</c>; this flag carries the "is its own awaited type" half of the bound.
        /// </summary>
        public bool IsAwaitedBound => IsAwaitedUtilityReference(Constraint);

        private static bool IsAwaitedUtilityReference(ITypeExpression? type)
        {
            switch (type)
            {
                case PhpTypeExpressionAst { TypeKind: PhpTypeKind.Simple, IsStatic: false } wrapper
                    when wrapper.Types?.GetAllNotNull().ToList() is { Count: 1 } members:
                    return !wrapper.IsNullable && IsAwaitedUtilityReference(members[0]);

                case PhpBuiltinTypeAst builtin:
                    return string.Equals(
                        builtin.Identifier?.TrimStart('\\'), "__Awaited", StringComparison.OrdinalIgnoreCase);

                case PhpNamedTypeAst named:
                    return (named.BoundSymbol ?? (named.Name as PhpNameAst)?.BoundSymbol)
                        is BuiltInUtilityTypeSymbol { Behavior: UtilityBehavior.Awaited };

                default:
                    return false;
            }
        }

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