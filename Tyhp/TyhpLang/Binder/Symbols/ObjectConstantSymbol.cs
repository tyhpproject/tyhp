using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectConstantSymbol :
        BaseSymbol,
        IObjectDeclarationScopeSymbol
    {
        public ITypeExpression? DeclaredType { get; internal set; }

        public IExpression? ValueExpression { get; internal set; }

        /// <summary>
        /// True when this constant is an enum case (e.g. <c>Color::Red</c>). An enum case's type is
        /// the enum itself, not its backing scalar type.
        /// </summary>
        public bool IsEnumCase { get; internal set; }

        public ObjectConstantSymbol(
            string name,
            string? sourceFile = null,
            IBase2Ast? declaringNode = null,
            MemberModifier visibility = MemberModifier.None
        )
            : base(name, SymbolType.ObjectConstant, declaringNode, sourceFile ?? string.Empty, visibility)
        {
        }
    }
}