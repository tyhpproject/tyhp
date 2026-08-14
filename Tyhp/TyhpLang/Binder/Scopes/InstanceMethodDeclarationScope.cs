using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class InstanceMethodDeclarationScope :
        BaseScope<
            ObjectDeclarationScope,
            IInstanceMethodDeclarationSymbol,
            IInstanceMethodDeclarationScopeChild,
            IInstanceMethodDeclarationScopeSymbol,
            InstanceMethodDeclarationScope
        >,
        IObjectDeclarationScopeChild,
        ICodeBlockScopeParent
    {
        public InstanceMethodDeclarationScope(ObjectDeclarationScope parent, IInstanceMethodDeclarationSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }

        void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
            => this.AddChildScope((IInstanceMethodDeclarationScopeChild)child);

        // TODO: only allow up to one single codeblock scope directly under this scope, this is the function body
    }
}