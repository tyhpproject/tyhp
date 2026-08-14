using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class AnonymousObjectDeclarationScope :
        BaseScope<
            GlobalScope,
            AnonymousObjectDeclarationSymbol,
            IAnonymousObjectDeclarationScopeChild,
            INoScopeSymbols,
            AnonymousObjectDeclarationScope
        >,
        IGlobalScopeChild
    {
        public AnonymousObjectDeclarationScope(GlobalScope parent, AnonymousObjectDeclarationSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }
    }
}