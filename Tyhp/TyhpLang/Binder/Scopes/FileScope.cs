using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class FileScope :
        BaseScope<
            GlobalScope,
            FileSymbol,
            IFileScopeChild,
            IBaseSymbol,
            FileScope
        >,
        IGlobalScopeChild,
        IObjectDeclarationScopeParent,
        IFunctionDeclarationScopeParent,
        ICodeBlockScopeParent,
        ILabelScopeParent
    {
        public FileScope(GlobalScope parent, FileSymbol symbol) : base(parent, symbol)
        {
        }

        /// <summary>
        /// Un-namespaced declarations live on per-file scopes. Enforce uniqueness across sibling
        /// file scopes for functions/classes/constants/type aliases (same rule as namespace blocks).
        /// </summary>
        public override bool AddChildSymbol(IBaseSymbol child)
        {
            if (child is BaseSymbol baseSymbol
                && baseSymbol.HasDeclaredName
                && IsCrossFileUniqueDeclaration(baseSymbol.SymbolType)
                && this.Parent is GlobalScope global)
            {
                foreach (var sibling in global.ChildScopes)
                {
                    if (sibling is not FileScope other || ReferenceEquals(other, this))
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
            => this.AddChildScope((IFileScopeChild)child);

        void ILabelScopeParent.AddLabelChildScope(LabelScope child)
            => this.AddChildScope(child);

        void IObjectDeclarationScopeParent.AddObjectDeclarationChildScope(ObjectDeclarationScope child)
            => this.AddChildScope(child);

        void IFunctionDeclarationScopeParent.AddFunctionDeclarationChildScope(FunctionDeclarationScope child)
            => this.AddChildScope(child);

        public string FileName => this.DeclarationSymbol.FileName;

        public string FileHash => this.DeclarationSymbol.FileHash;

        public string SourceFile => this.DeclarationSymbol.SourceFile;

        public bool TryAddFileDeclareDirective(
            string key,
            string value,
            out string? validationMessage
        )
        {
            return this.DeclarationSymbol.TryAddFileDeclareDirective(key, value, out validationMessage);
        }

        public FileScope AddFileDeclareDirective(string key, string value)
        {
            this.DeclarationSymbol.AddFileDeclareDirective(key, value);
            return this;
        }

        public bool TryGetFileDeclareDirective(string key, out string? value)
        {
            return this.DeclarationSymbol.TryGetFileDeclareDirective(key, out value);
        }
    }
}
