using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class NamespaceScope :
        BaseScope<
            GlobalScope,
            NamespaceSymbol,
            INamespaceScopeChild,
            INoScopeSymbols,
            NamespaceScope
        >,
        IGlobalScopeChild
    {
        public NamespaceScope(GlobalScope parent, NamespaceSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }
    }
}