
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public abstract class BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf> : 
        IBaseScope<
            TParent,
            TDeclarationSymbol,
            TChildScopes,
            TChildSymbols,
            TSelf
        >
        where TDeclarationSymbol : IBaseSymbol
        where TParent : class?, IBaseScope?
        where TSelf : BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
        where TChildScopes : class, IBaseScope<TSelf>
        where TChildSymbols : IBaseSymbol
    {
        private List<TChildScopes>? _childScopes;
        private List<IBaseScope>? _additionalChildScopes;
        private List<TChildSymbols>? _childSymbols;
        private Dictionary<string, TChildSymbols>? _childSymbolIndex;

        // PHP keeps constants in their own (case-sensitive) namespace, separate from the
        // case-insensitive function/class namespace. Indexing them apart prevents false
        // collisions such as the constant `HASH_HMAC` clashing with the function `hash_hmac`.
        private Dictionary<string, TChildSymbols>? _constantSymbolIndex;

        // PHP also keeps functions in their own (case-insensitive) namespace, separate from
        // class-like symbols (classes/interfaces/traits/enums). Indexing them apart prevents false
        // collisions such as the function `decimal` clashing with the class `Decimal`.
        private Dictionary<string, TChildSymbols>? _functionSymbolIndex;

        // Static fields are intentionally per-closed-generic-type to isolate cache entries by scope kind.
        private static readonly object NamespacePathCacheLock = new();
        private const int NamespacePathCacheResetThreshold = 8192;
        private static int _namespacePathCacheAddCount;
        private static volatile ConditionalWeakTable<IBaseScope, NamespacePathCacheEntry> _namespacePathCache = new();

        private sealed class NamespacePathCacheEntry
        {
            public NamespacePathCacheEntry(IBaseScope? parentScope, string namespacePath)
            {
                this.ParentScope = parentScope;
                this.NamespacePath = namespacePath;
            }

            public IBaseScope? ParentScope { get; }
            public string NamespacePath { get; }
        }

        public virtual TDeclarationSymbol? DeclarationSymbol { get; protected set; } = default;
        public virtual TParent? Parent { get; set; }
        
        public virtual IReadOnlyList<TChildScopes> ChildScopes => (IReadOnlyList<TChildScopes>?)this._childScopes ?? Array.Empty<TChildScopes>();
        public virtual IReadOnlyList<TChildSymbols> ChildSymbols => (IReadOnlyList<TChildSymbols>?)this._childSymbols ?? Array.Empty<TChildSymbols>();

        IBaseScope? IBaseScope.ParentScope => this.Parent as IBaseScope;

        IBaseSymbol? IBaseScope.DeclarationSymbol => this.DeclarationSymbol;

        IBaseSymbol? IBaseScope.FindChildSymbolByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Constants are matched case-sensitively and take precedence on an exact-name hit, so a
            // look-up of an all-caps constant name (e.g. `HASH_HMAC`) is not captured by a
            // case-insensitive function of the same letters (e.g. `hash_hmac`).
            if (this._constantSymbolIndex != null && this._constantSymbolIndex.TryGetValue(name, out var constantSymbol))
            {
                return constantSymbol;
            }

            if (this._childSymbolIndex != null && this._childSymbolIndex.TryGetValue(name, out var symbol))
            {
                return symbol;
            }

            // Functions live in a separate namespace and are searched last: for any valid program a
            // name resolves to a single symbol kind, so this only matters when a class-like symbol
            // and a same-named function legitimately coexist (e.g. `class Decimal` + `function
            // decimal`), where the class-like symbol is preferred by name lookup.
            if (this._functionSymbolIndex != null && this._functionSymbolIndex.TryGetValue(name, out var functionSymbol))
            {
                return functionSymbol;
            }

            return null;
        }

        IEnumerable<IBaseSymbol> IBaseScope.GetAllChildSymbols()
        {
            if (this._childSymbols == null) return Enumerable.Empty<IBaseSymbol>();
            return this._childSymbols.Cast<IBaseSymbol>();
        }

        IEnumerable<IBaseScope> IBaseScope.GetAllChildScopes()
        {
            var hasTyped = this._childScopes != null && this._childScopes.Count > 0;
            var hasAdditional = this._additionalChildScopes != null && this._additionalChildScopes.Count > 0;

            if (!hasTyped && !hasAdditional)
                return Enumerable.Empty<IBaseScope>();

            if (!hasAdditional)
                return this._childScopes!.Cast<IBaseScope>();

            if (!hasTyped)
                return this._additionalChildScopes!;

            return this._childScopes!.Cast<IBaseScope>().Concat(this._additionalChildScopes!);
        }

        public BaseScope(TParent? parent = null, TDeclarationSymbol? declarationSymbol = default)
        {
            this.Parent = parent;
            this.DeclarationSymbol = declarationSymbol;
        }

        public virtual TSelf AddChildScope(TChildScopes child)
        {
            child.Parent = (TSelf)this;
            BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>.InvalidateNamespacePathCacheOnParentReassignment(child);

            if (child.DeclarationSymbol is BaseSymbol baseSymbol)
            {
                // ContainingScope points to the parent scope (where the symbol is declared
                // in the namespace hierarchy), not the child scope the symbol creates.
                baseSymbol.ContainingScope = this;
                baseSymbol.FullyQualifiedName = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                    .GetFullyQualifiedNameFor(this, baseSymbol.Name);
            }

            this.ChildScopesList.Add(child);
            return (TSelf)this;
        }

        public virtual bool AddChildSymbol(TChildSymbols child)
        {
            // Operator overloads intentionally share a single declared name (the operator token,
            // e.g. "+", "convert") across multiple symbols that differ by parameter signature or
            // return type. They are discovered by enumeration (GetAllChildSymbols) and disambiguated
            // by the operator-overload resolver, so they must bypass the by-name uniqueness index
            // (both the duplicate check below and the index insertion further down).
            var isOverloadableMember = child is BaseSymbol overloadCandidate
                && overloadCandidate.SymbolType == SymbolType.ObjectOperatorOverload;

            if (!isOverloadableMember && child is BaseSymbol baseSymbol && baseSymbol.HasDeclaredName)
            {
                var nameIndex = this.GetNameIndexFor(baseSymbol.SymbolType);
                if (nameIndex != null && nameIndex.TryGetValue(baseSymbol.Name, out var existingSymbol))
                {
                    var computedFqn = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                        .GetFullyQualifiedNameFor(this, baseSymbol.Name);
                    this.OnDuplicateChildSymbol(existingSymbol, child, computedFqn);
                    return false;
                }
            }

            if (child is BaseSymbol childSymbol && this.DeclarationSymbol is BaseSymbol scopeSymbol)
            {
                var allowedChildren = SymbolTypeHelper.GetAllowedChildren(scopeSymbol.SymbolType);
                if (allowedChildren.Count > 0)
                {
                    (SymbolType SymbolType, bool AllowMultiple)? match = null;
                    for (var i = 0; i < allowedChildren.Count; i++)
                    {
                        if (allowedChildren[i].SymbolType == childSymbol.SymbolType)
                        {
                            match = allowedChildren[i];
                            break;
                        }
                    }

                    if (match == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Binder] Symbol '{childSymbol.Name}' (SymbolType.{childSymbol.SymbolType}) rejected: not allowed in scope SymbolType.{scopeSymbol.SymbolType}");
                        return false;
                    }

                    if (!match.Value.AllowMultiple)
                    {
                        var list = this.ChildSymbolList;
                        for (var i = 0; i < list.Count; i++)
                        {
                            if (list[i] is BaseSymbol existing && existing.SymbolType == childSymbol.SymbolType)
                            {
                                var computedFqn = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                                    .GetFullyQualifiedNameFor(this, childSymbol.Name);
                                this.OnDuplicateChildSymbol(list[i], child, computedFqn);
                                return false;
                            }
                        }
                    }
                }
            }

            if (child is BaseSymbol childBaseSymbol)
            {
                childBaseSymbol.ContainingScope = this;
            }

            if (child is BaseSymbol duplicateCheckSymbol)
            {
                duplicateCheckSymbol.FullyQualifiedName = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                    .GetFullyQualifiedNameFor(this, duplicateCheckSymbol.Name);
                if (!isOverloadableMember && duplicateCheckSymbol.HasDeclaredName)
                {
                    this.GetOrCreateNameIndexFor(duplicateCheckSymbol.SymbolType)[duplicateCheckSymbol.Name] = child;
                }
            }

            this.ChildSymbolList.Add(child);
            return true;
        }

        private List<TChildScopes> ChildScopesList =>
            this._childScopes ??= new List<TChildScopes>();

        private List<TChildSymbols> ChildSymbolList =>
            this._childSymbols ??= new List<TChildSymbols>();

        /// <summary>
        /// Adds a child scope through a marker parent interface when C# generic invariance
        /// prevents the typed <see cref="AddChildScope"/> from accepting the child directly.
        /// The child's parent is already set by its constructor; this handles ContainingScope,
        /// FQN computation, and storage in an auxiliary list that <see cref="IBaseScope.GetAllChildScopes"/>
        /// includes alongside the typed list.
        /// </summary>
        protected TSelf AddChildScopeFromMarkerInterface(IBaseScope child)
        {
            BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                .InvalidateNamespacePathCacheOnParentReassignment(child);

            if (child.DeclarationSymbol is BaseSymbol baseSymbol)
            {
                baseSymbol.ContainingScope = this;
                baseSymbol.FullyQualifiedName = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                    .GetFullyQualifiedNameFor(this, baseSymbol.Name);
            }

            if (child is TChildScopes typedChild)
            {
                this.ChildScopesList.Add(typedChild);
            }
            else
            {
                (this._additionalChildScopes ??= new List<IBaseScope>()).Add(child);
            }

            return (TSelf)this;
        }

        private Dictionary<string, TChildSymbols> ChildSymbolIndex =>
            this._childSymbolIndex ??= new Dictionary<string, TChildSymbols>(StringComparer.OrdinalIgnoreCase);

        // Case-sensitive to honor PHP's constant naming (constant names are case-sensitive).
        private Dictionary<string, TChildSymbols> ConstantSymbolIndex =>
            this._constantSymbolIndex ??= new Dictionary<string, TChildSymbols>(StringComparer.Ordinal);

        // Case-insensitive, matching PHP function-name semantics.
        private Dictionary<string, TChildSymbols> FunctionSymbolIndex =>
            this._functionSymbolIndex ??= new Dictionary<string, TChildSymbols>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the name index a symbol of the given kind is registered in (may be null if not yet
        /// created). PHP has three symbol namespaces: constants including class constants (case-sensitive),
        /// functions (case-insensitive), and class-like symbols / methods / properties (case-insensitive).
        /// </summary>
        private Dictionary<string, TChildSymbols>? GetNameIndexFor(SymbolType symbolType)
            => IsConstantNamespaceSymbol(symbolType) ? this._constantSymbolIndex
                : IsFunctionNamespaceSymbol(symbolType) ? this._functionSymbolIndex
                : this._childSymbolIndex;

        /// <summary>
        /// Looks up a child already registered in the same PHP symbol namespace (constants / functions /
        /// class-likes) as <paramref name="symbolType"/>. Used for cross-file uniqueness checks where
        /// <see cref="IBaseScope.FindChildSymbolByName"/> would prefer a class over a same-named function.
        /// </summary>
        protected bool TryGetChildInPhpSymbolNamespace(
            string name,
            SymbolType symbolType,
            out TChildSymbols existing)
        {
            existing = default!;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var index = this.GetNameIndexFor(symbolType);
            if (index != null && index.TryGetValue(name, out var found) && found is not null)
            {
                existing = found;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns (creating if needed) the name index a symbol of the given kind is registered in.
        /// </summary>
        private Dictionary<string, TChildSymbols> GetOrCreateNameIndexFor(SymbolType symbolType)
            => IsConstantNamespaceSymbol(symbolType) ? this.ConstantSymbolIndex
                : IsFunctionNamespaceSymbol(symbolType) ? this.FunctionSymbolIndex
                : this.ChildSymbolIndex;

        private static bool IsConstantNamespaceSymbol(SymbolType symbolType)
            => symbolType is SymbolType.Constant
                or SymbolType.MagicConstant
                // Class constants / enum cases share PHP's case-sensitive constant namespace,
                // separate from methods (so `const TAG` and `tag()` may coexist).
                or SymbolType.ObjectConstant;

        private static bool IsFunctionNamespaceSymbol(SymbolType symbolType)
            => symbolType is SymbolType.FunctionDeclaration or SymbolType.BuiltInFunction;

        /// <summary>
        /// Declarations that must be unique across all files contributing to the same namespace
        /// (or the global namespace via file scopes). File-local symbols such as <c>use</c> aliases
        /// and variables are excluded.
        /// </summary>
        protected static bool IsCrossFileUniqueDeclaration(SymbolType symbolType)
            => symbolType is SymbolType.FunctionDeclaration
                or SymbolType.ObjectTypeDeclaration
                or SymbolType.Constant
                or SymbolType.TypeAlias;

        /// <summary>
        /// True when a sibling-scope hit from <see cref="TryGetChildInPhpSymbolNamespace"/> should
        /// count as a cross-file duplicate. <c>use</c> aliases share the class-like name index with
        /// types but are file-local — they must not collide with a real declaration in another file.
        /// </summary>
        protected static bool IsCrossFileDuplicateHit(object? existing)
            => existing is BaseSymbol existingBase
                && IsCrossFileUniqueDeclaration(existingBase.SymbolType);

        private static void UpdateNamespacePathCache(IBaseScope scope, string namespacePath)
        {
            lock (NamespacePathCacheLock)
            {
                if (_namespacePathCacheAddCount >= NamespacePathCacheResetThreshold)
                {
                    _namespacePathCache = new ConditionalWeakTable<IBaseScope, NamespacePathCacheEntry>();
                    _namespacePathCacheAddCount = 0;
                }

                _namespacePathCache.Remove(scope);
                _namespacePathCache.Add(scope, new NamespacePathCacheEntry(scope.ParentScope, namespacePath));
                _namespacePathCacheAddCount++;
            }
        }

        private static void InvalidateNamespacePathCacheOnParentReassignment(IBaseScope child)
        {
            // No explicit cache invalidation needed: GetFullyQualifiedNameFor validates the cached
            // ParentScope identity via ReferenceEquals on every read and recomputes on mismatch,
            // making eager invalidation unnecessary.
            _ = child;
        }

        /// <summary>
        /// Called when a duplicate-named symbol is added to this scope.
        /// The base implementation keeps scope insertion non-fatal while allowing
        /// derived phases to report diagnostics.
        /// </summary>
        /// <param name="existingSymbol">The first symbol already bound under the same name.</param>
        /// <param name="duplicateSymbol">The replacement symbol that was skipped.</param>
        protected virtual void OnDuplicateChildSymbol(
            TChildSymbols existingSymbol,
            TChildSymbols duplicateSymbol,
            string computedFullyQualifiedName
        )
        {
            // Override to emit diagnostics (e.g. duplicate declaration) when duplicate is rejected.
            _ = existingSymbol;
            _ = duplicateSymbol;
            _ = computedFullyQualifiedName;
        }

        protected static string GetFullyQualifiedNameFor(
            IBaseScope scope,
            string name
        )
        {
            var namespacePath = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>.GetNamespacePath(scope);
            return string.IsNullOrWhiteSpace(name)
                ? namespacePath
                : string.IsNullOrWhiteSpace(namespacePath)
                    ? "\\" + name
                    : $"{namespacePath}\\{name}";
        }

        private static string GetNamespacePath(IBaseScope? scope, int maxDepth = 500)
        {
            if (scope == null || maxDepth <= 0)
            {
                return string.Empty;
            }

            if (
                BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>._namespacePathCache
                    .TryGetValue(scope, out var cachedPath) &&
                ReferenceEquals(cachedPath.ParentScope, scope.ParentScope))
            {
                return cachedPath.NamespacePath;
            }

            var parentPath = BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                .GetNamespacePath(scope.ParentScope, maxDepth - 1);

            var declarationSymbol = scope.DeclarationSymbol;
            // Only NamespaceSymbol contributes a segment to the namespace path. FileScope, CodeBlockScope,
            // and other scope types are transparent to FQN computation, consistent with PHP namespace
            // semantics where files do not define namespace boundaries.
            if (!(declarationSymbol is NamespaceSymbol namespaceSymbol))
            {
                BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                    .UpdateNamespacePathCache(scope, parentPath);
                return parentPath;
            }

            var namespaceName = namespaceSymbol.Name?.Trim('\\');
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
                    .UpdateNamespacePathCache(scope, parentPath);
                return parentPath;
            }

            var namespacePath = string.IsNullOrWhiteSpace(parentPath)
                ? "\\" + namespaceName
                : $"{parentPath}\\{namespaceName}";
            BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>.UpdateNamespacePathCache(scope, namespacePath);
            return namespacePath;
        }
    }
}