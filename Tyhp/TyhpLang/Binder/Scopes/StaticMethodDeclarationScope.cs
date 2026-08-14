using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class StaticMethodDeclarationScope :
        BaseScope<
            ObjectDeclarationScope,
            IStaticMethodDeclarationSymbol,
            IStaticMethodDeclarationScopeChild,
            IStaticMethodDeclarationScopeSymbol,
            StaticMethodDeclarationScope
        >,
        IObjectDeclarationScopeChild,
        ICodeBlockScopeParent
    {
        public StaticMethodDeclarationScope(ObjectDeclarationScope parent, IStaticMethodDeclarationSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }

        void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
            => this.AddChildScope((IStaticMethodDeclarationScopeChild)child);

        // TODO: only allow up to one single codeblock scope directly under this scope, this is the function body
    }
}