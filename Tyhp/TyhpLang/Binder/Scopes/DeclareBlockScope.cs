using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class DeclareBlockScope :
        CodeBlockScope<DeclareBlockSymbol>
    {
        public DeclareBlockScope(ICodeBlockScopeParent parent, DeclareBlockSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }
    }
}
