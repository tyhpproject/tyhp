using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class FunctionDeclarationScope :
        BaseScope<
            IFunctionDeclarationScopeParent,
            FunctionDeclarationSymbol,
            IFunctionDeclarationScopeChild,
            IFunctionDeclarationScopeSymbol,
            FunctionDeclarationScope
        >,
        INamespaceBlockScopeChild,
        ICodeBlockScopeChild,
        IFileScopeChild,
        ICodeBlockScopeParent
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

        public FunctionDeclarationScope(IFunctionDeclarationScopeParent parent, FunctionDeclarationSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }

        void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
            => this.AddChildScope((IFunctionDeclarationScopeChild)child);

        // TODO: only allow up to one single codeblock scope directly under this scope, this is the function body
    }
}