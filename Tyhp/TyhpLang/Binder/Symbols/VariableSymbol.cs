using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class VariableSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol,
        ICodeBlockScopeSymbol,
        IFunctionDeclarationScopeSymbol,
        IAnonymousFunctionScopeSymbol,
        IInstanceMethodDeclarationScopeSymbol,
        IStaticMethodDeclarationScopeSymbol
    {
        public ITypeExpression? DeclaredType { get; internal set; }

        public bool IsParameter { get; internal set; }

        public IExpression? DefaultValue { get; internal set; }

        public bool IsDisposable { get; internal set; }

        public bool IsPromotedProperty { get; internal set; }

        public bool IsRef { get; internal set; }

        public VariableSymbol(
            string name,
            IBase2Ast? declaringNode = null,
            string sourceFile = "",
            MemberModifier visibility = MemberModifier.None
        )
            : base(name, SymbolType.Variable, declaringNode, sourceFile, visibility)
        {
        }
    }
}