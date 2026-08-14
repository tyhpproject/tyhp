using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class NamespaceBlockScope :
        BaseScope<
            NamespaceScope,
            NamespaceBlockSymbol,
            INamespaceBlockScopeChild,
            INamespaceBlockScopeSymbol,
            NamespaceBlockScope
        >,
        INamespaceScopeChild,
        IFunctionDeclarationScopeParent,
        ICodeBlockScopeParent,
        ILabelScopeParent,
        IObjectDeclarationScopeParent
    {
        public NamespaceBlockScope(NamespaceScope parent, NamespaceBlockSymbol symbol) : base(parent, symbol)
        {
            // ctor
        }

        /// <summary>
        /// Each source file gets its own <see cref="NamespaceBlockScope"/> under a shared
        /// <see cref="NamespaceScope"/> (so file-local <c>use</c> aliases stay isolated). Declarations
        /// that PHP/Tyhp require to be unique in a namespace must still collide across those sibling
        /// blocks — check siblings before accepting the symbol, ignoring file-local hits such as
        /// <c>use</c> aliases that share the class-like name index.
        /// </summary>
        public override bool AddChildSymbol(INamespaceBlockScopeSymbol child)
        {
            if (child is BaseSymbol baseSymbol
                && baseSymbol.HasDeclaredName
                && IsCrossFileUniqueDeclaration(baseSymbol.SymbolType)
                && this.Parent is NamespaceScope nsParent)
            {
                foreach (var sibling in nsParent.ChildScopes)
                {
                    if (sibling is not NamespaceBlockScope other || ReferenceEquals(other, this))
                    {
                        continue;
                    }

                    if (other.TryGetChildInPhpSymbolNamespace(
                            baseSymbol.Name,
                            baseSymbol.SymbolType,
                            out var existing)
                        && IsCrossFileDuplicateHit(existing))
                    {
                        var computedFqn = GetFullyQualifiedNameFor(this, baseSymbol.Name);
                        this.OnDuplicateChildSymbol(existing, child, computedFqn);
                        return false;
                    }
                }
            }

            return base.AddChildSymbol(child);
        }

        void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
            => this.AddChildScope((INamespaceBlockScopeChild)child);

        void ILabelScopeParent.AddLabelChildScope(LabelScope child)
            => this.AddChildScope(child);

        void IObjectDeclarationScopeParent.AddObjectDeclarationChildScope(ObjectDeclarationScope child)
            => this.AddChildScope(child);

        void IFunctionDeclarationScopeParent.AddFunctionDeclarationChildScope(FunctionDeclarationScope child)
            => this.AddChildScope(child);
    }
}