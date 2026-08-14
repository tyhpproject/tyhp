using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class LabelScope :
        BaseScope<
            ILabelScopeParent,
            LabelSymbol,
            INoScopeChild<LabelScope>,
            INoScopeSymbols,
            LabelScope
        >,
        INamespaceBlockScopeChild,
        ICodeBlockScopeChild,
        IDeclareBlockScopeChild,
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
        DeclareBlockScope? IBaseScope<DeclareBlockScope>.Parent {
            get => this.Parent as DeclareBlockScope;
            set => this.Parent = value;
        }
        FileScope? IBaseScope<FileScope>.Parent {
            get => this.Parent as FileScope;
            set => this.Parent = value;
        }

        public LabelScope(ILabelScopeParent parent, LabelSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }
    }
}