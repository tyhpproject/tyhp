using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder.Resolution
{
    /// <summary>
    /// Collects in-scope identifier candidates for <see cref="Tyhp.Domain.Diagnostics.DidYouMean"/>
    /// suggestions on unknown-symbol / unknown-type / unknown-member diagnostics.
    /// </summary>
    public static class InScopeNameCandidates
    {
        private const int DefaultMaxCandidates = 512;

        /// <summary>
        /// Collects type-like names visible from <paramref name="fromScope"/>: object declarations,
        /// type aliases, and import aliases along the enclosing scope chain (including sibling
        /// namespace blocks under a <see cref="NamespaceScope"/>).
        /// </summary>
        public static IReadOnlyList<string> CollectTypeNames(
            IBaseScope? fromScope,
            int maxCandidates = DefaultMaxCandidates)
        {
            if (fromScope is null || maxCandidates <= 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(Math.Min(64, maxCandidates));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var scope = fromScope; scope is not null && result.Count < maxCandidates; scope = scope.ParentScope)
            {
                AddTypeSymbols(scope.GetAllChildSymbols(), result, seen, maxCandidates);

                // Namespace scopes hold block scopes as children; sibling blocks declare types
                // that name resolution can see, so include them as candidates too.
                if (scope is NamespaceScope)
                {
                    foreach (var childScope in scope.GetAllChildScopes())
                    {
                        if (result.Count >= maxCandidates)
                        {
                            break;
                        }

                        AddTypeSymbols(childScope.GetAllChildSymbols(), result, seen, maxCandidates);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Collects bare property names from <paramref name="objectDecl"/> and its inheritance
        /// chain (via <paramref name="resolveParent"/>). Leading <c>$</c> is stripped so results
        /// match how users write <c>with</c> keys and member names.
        /// </summary>
        public static IReadOnlyList<string> CollectPropertyNames(
            ObjectDeclarationSymbol? objectDecl,
            Func<ObjectDeclarationSymbol, ObjectDeclarationSymbol?> resolveParent,
            int maxCandidates = DefaultMaxCandidates)
        {
            if (objectDecl is null || resolveParent is null || maxCandidates <= 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(Math.Min(32, maxCandidates));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<ObjectDeclarationSymbol>();

            for (var current = objectDecl;
                 current is not null && result.Count < maxCandidates;
                 current = resolveParent(current))
            {
                if (!visited.Add(current))
                {
                    break;
                }

                foreach (var (key, member) in current.Members)
                {
                    if (member is not ObjectPropertySymbol)
                    {
                        continue;
                    }

                    var bare = key.StartsWith('$') ? key[1..] : key;
                    if (string.IsNullOrEmpty(bare) || !seen.Add(bare))
                    {
                        continue;
                    }

                    result.Add(bare);
                    if (result.Count >= maxCandidates)
                    {
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Collects parameter names (leading <c>$</c> stripped) for unknown-named-argument suggestions.
        /// </summary>
        public static IReadOnlyList<string> CollectParameterNames(
            IEnumerable<ParameterInfo>? parameters,
            int maxCandidates = DefaultMaxCandidates)
        {
            if (parameters is null || maxCandidates <= 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in parameters)
            {
                if (result.Count >= maxCandidates)
                {
                    break;
                }

                var bare = parameter.Name?.TrimStart('$');
                if (string.IsNullOrEmpty(bare) || !seen.Add(bare))
                {
                    continue;
                }

                result.Add(bare);
            }

            return result;
        }

        private static void AddTypeSymbols(
            IEnumerable<IBaseSymbol> symbols,
            List<string> result,
            HashSet<string> seen,
            int maxCandidates)
        {
            foreach (var symbol in symbols)
            {
                if (result.Count >= maxCandidates)
                {
                    return;
                }

                switch (symbol)
                {
                    case ObjectDeclarationSymbol:
                    case TypeAliasSymbol:
                    case ObjectTypeAliasSymbol:
                        TryAdd(symbol.Name, result, seen);
                        break;

                    case UseIncludeSymbol use:
                        TryAdd(use.AliasName ?? use.Name, result, seen);
                        if (use.ImportedNameSegments is { Length: > 0 })
                        {
                            TryAdd(use.ImportedNameSegments[^1], result, seen);
                        }

                        break;
                }
            }
        }

        private static void TryAdd(string? name, List<string> result, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var trimmed = name.TrimStart('\\');
            if (trimmed.Length == 0)
            {
                return;
            }

            // Prefer the simple (unqualified) name for suggestions.
            var slash = trimmed.LastIndexOf('\\');
            var simple = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
            if (simple.Length == 0 || !seen.Add(simple))
            {
                return;
            }

            result.Add(simple);
        }
    }
}
