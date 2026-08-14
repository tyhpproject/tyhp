using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class ObjectDeclarationScope :
        BaseScope<
            IObjectDeclarationScopeParent,
            ObjectDeclarationSymbol,
            IObjectDeclarationScopeChild,
            IObjectDeclarationScopeSymbol,
            ObjectDeclarationScope
        >,
        INamespaceBlockScopeChild,
        ICodeBlockScopeChild,
        IFileScopeChild
    {
        NamespaceBlockScope? IBaseScope<NamespaceBlockScope>.Parent {
            get => this.Parent as NamespaceBlockScope;
            set => this.Parent = value;
        }
        CodeBlockScope? IBaseScope<CodeBlockScope>.Parent {
            get => this.Parent as CodeBlockScope;
            set => this.Parent = value;
        }
        FileScope? IBaseScope<FileScope>.Parent {
            get => this.Parent as FileScope;
            set => this.Parent = value;
        }

        public ObjectDeclarationScope(IObjectDeclarationScopeParent parent, ObjectDeclarationSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }
    }
}