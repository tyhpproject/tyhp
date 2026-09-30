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
        /// Standalone <c>extension { }</c> declarations in this file (same-file auto-activation).
        /// </summary>
        public List<ObjectDeclarationSymbol> DeclaredExtensions { get; } = [];

        /// <summary>
        /// Extensions activated by a non-global <c>use extension</c> in this file.
        /// </summary>
        public List<ObjectDeclarationSymbol> ImportedExtensions { get; } = [];

        /// <summary>File-local postfix <c>hide</c> members from <c>use extension</c>.</summary>
        public HashSet<string>? ExtensionUseHiddenMembers { get; set; }

        /// <summary>File-local <c>insteadof</c> rules from <c>use extension</c>.</summary>
        public Dictionary<string, string>? ExtensionUseMethodPrecedence { get; set; }

        /// <summary>File-local <c>as</c> aliases from <c>use extension</c>.</summary>
        public Dictionary<string, (string?, string)>? ExtensionUseMethodAliases { get; set; }

        /// <summary>
        /// Un-namespaced declarations live on per-file scopes. Enforce uniqueness across sibling
        /// file scopes for functions/classes/constants/type aliases (same rule as namespace blocks).
        /// </summary>
        public override bool TryAddChildSymbol(IBaseSymbol child, out IBaseSymbol? existing)
        {
            existing = null;
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
                            out var found)
                        && IsCrossFileDuplicateHit(found))
                    {
                        existing = found;
                        var computedFqn = GetFullyQualifiedNameFor(this, baseSymbol.Name);
                        this.OnDuplicateChildSymbol(found, child, computedFqn);
                        return false;
                    }
                }
            }

            return base.TryAddChildSymbol(child, out existing);
        }

        /// <inheritdoc />
        public override bool TryOccupyFunctionNamespace(IBaseSymbol symbol)
        {
            if (symbol is BaseSymbol baseSymbol
                && baseSymbol.HasDeclaredName
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
                            SymbolType.FunctionDeclaration,
                            out var existing)
                        && !ReferenceEquals(existing, symbol))
                    {
                        return false;
                    }
                }
            }

            return base.TryOccupyFunctionNamespace(symbol);
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
