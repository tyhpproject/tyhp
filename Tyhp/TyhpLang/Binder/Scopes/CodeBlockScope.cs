using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class CodeBlockScope : CodeBlockScope<CodeBlockSymbol>
    {
        public CodeBlockScope(ICodeBlockScopeParent parent, CodeBlockSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }

        public int SyntheticCounter { get; set; }
    }

    abstract public class CodeBlockScope<TBaseSymbol> :
        BaseScope<
            ICodeBlockScopeParent,
            TBaseSymbol,
            IBaseScope<CodeBlockScope<TBaseSymbol>>,
            ICodeBlockScopeSymbol,
            CodeBlockScope<TBaseSymbol>
        >,
        ICodeBlockScopeChild,
        IDeclareBlockScopeChild,
        INamespaceBlockScopeChild,
        IAnonymousFunctionScopeChild,
        IFunctionDeclarationScopeChild,
        IInstanceMethodDeclarationScopeChild,
        IStaticMethodDeclarationScopeChild,
        IFileScopeChild,
        ICodeBlockScopeParent,
        IFunctionDeclarationScopeParent,
        ILabelScopeParent,
        IObjectDeclarationScopeParent
        where TBaseSymbol : CodeBlockSymbol
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

        public CodeBlockScope(ICodeBlockScopeParent parent, TBaseSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }

        // TChildScopes is IBaseScope<CodeBlockScope<TBaseSymbol>>, but child scopes implement
        // IBaseScope<CodeBlockScope> (non-generic). C# generic invariance prevents direct AddChildScope
        // because CodeBlockScope and CodeBlockScope<TBaseSymbol> are different types to the compiler.
        void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
            => this.AddChildScopeFromMarkerInterface(child);

        void ILabelScopeParent.AddLabelChildScope(LabelScope child)
            => this.AddChildScopeFromMarkerInterface(child);

        void IObjectDeclarationScopeParent.AddObjectDeclarationChildScope(ObjectDeclarationScope child)
            => this.AddChildScopeFromMarkerInterface(child);

        void IFunctionDeclarationScopeParent.AddFunctionDeclarationChildScope(FunctionDeclarationScope child)
            => this.AddChildScopeFromMarkerInterface(child);

        // TODO: Check if ObjectDeclaration or FunctionDeclaration child scopes are allowed in this scope
        // TODO: only allowed if this code block has parents that are CodeBlockScope[] or DeclareBlockScope[] at any depth or combination up to a NamespaceBlockScope
        // TODO: if it has any other parents in the scope tree, then it is not allowed
    }
}
