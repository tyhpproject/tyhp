using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class AnonymousFunctionScope :
        BaseScope<
            ICodeBlockScopeParent,
            AnonymousFunctionSymbol,
            IAnonymousFunctionScopeChild,
            IAnonymousFunctionScopeSymbol,
            AnonymousFunctionScope
        >,
        ICodeBlockScopeChild,
        IDeclareBlockScopeChild,
        INamespaceBlockScopeChild,
        IAnonymousFunctionScopeChild,
        IFunctionDeclarationScopeChild,
        IInstanceMethodDeclarationScopeChild,
        IStaticMethodDeclarationScopeChild,
        IFileScopeChild,
        IGlobalScopeChild,
        ICodeBlockScopeParent
    {
        NamespaceBlockScope? IBaseScope<NamespaceBlockScope>.Parent {
            get => this.Parent as NamespaceBlockScope;
            set => this.Parent = value;
        }
        AnonymousFunctionScope? IBaseScope<AnonymousFunctionScope>.Parent {
            get => this.Parent as AnonymousFunctionScope;
            set => this.Parent = value;
        }
        CodeBlockScope? IBaseScope<CodeBlockScope>.Parent {
            get => this.Parent as CodeBlockScope;
            set => this.Parent = value;
        }
        DeclareBlockScope? IBaseScope<DeclareBlockScope>.Parent {
            get => this.Parent as DeclareBlockScope;
            set => this.Parent = value;
        }
        FunctionDeclarationScope? IBaseScope<FunctionDeclarationScope>.Parent {
            get => this.Parent as FunctionDeclarationScope;
            set => this.Parent = value;
        }
        InstanceMethodDeclarationScope? IBaseScope<InstanceMethodDeclarationScope>.Parent {
            get => this.Parent as InstanceMethodDeclarationScope;
            set => this.Parent = value;
        }
        StaticMethodDeclarationScope? IBaseScope<StaticMethodDeclarationScope>.Parent {
            get => this.Parent as StaticMethodDeclarationScope;
            set => this.Parent = value;
        }
        FileScope? IBaseScope<FileScope>.Parent {
            get => this.Parent as FileScope;
            set => this.Parent = value;
        }
        GlobalScope? IBaseScope<GlobalScope>.Parent {
            get => this.Parent as GlobalScope;
            set => this.Parent = value is null ? null : (ICodeBlockScopeParent)(object)value;
        }

        public AnonymousFunctionScope(ICodeBlockScopeParent parent, AnonymousFunctionSymbol symbol) : base(parent, symbol)
        {
        }

        void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
            => this.AddChildScope((IAnonymousFunctionScopeChild)child);
    }
}
