using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ConstantSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol,
        ICodeBlockScopeSymbol
    {
        public ITypeExpression? DeclaredType { get; internal set; }

        public IExpression? ValueExpression { get; protected set; }

        public ConstantSymbol(
            string name,
            string? sourceFile = null,
            IBase2Ast? declaringNode = null
        )
            : base(name, SymbolType.Constant, declaringNode: declaringNode, sourceFile: sourceFile ?? string.Empty)
        {
        }
    }
}