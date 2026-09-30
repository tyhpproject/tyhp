namespace Tyhp.LanguageServer.Analysis
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Enum;

    /// <summary>
    /// Workspace-wide symbol search over the binder <see cref="GlobalScope"/>.
    /// </summary>
    internal static class WorkspaceSymbolSearch
    {
        internal const int MaxResults = 100;

        /// <summary>
        /// Finds declarations matching <paramref name="query"/> (case-insensitive
        /// substring, prefix, or fuzzy subsequence). Empty query returns up to
        /// <see cref="MaxResults"/> declarations sorted by name.
        /// </summary>
        public static SymbolInformation[] Search(
            string? query,
            GlobalScope? globalScope,
            WorkspaceManager workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            if (globalScope is null)
            {
                return [];
            }

            string needle = (query ?? string.Empty).Trim();
            bool qualified = needle.Contains('\\', StringComparison.Ordinal);
            var matches = new List<(int Score, SymbolInformation Symbol)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (BaseSymbol symbol in UseStatementEdits.EnumerateAllSymbols(globalScope))
            {
                if (!IsWorkspaceSymbol(symbol))
                {
                    continue;
                }

                int score = Score(needle, symbol, qualified);
                if (score <= 0)
                {
                    continue;
                }

                string identity = symbol.FullyQualifiedName + ":" + symbol.SymbolType + ":" + symbol.SourceFile + ":" + symbol.Line;
                if (!seen.Add(identity))
                {
                    continue;
                }

                SymbolInformation? info = ToInformation(symbol, workspace);
                if (info is null)
                {
                    continue;
                }

                matches.Add((score, info));
            }

            matches.Sort(static (left, right) =>
            {
                int byScore = right.Score.CompareTo(left.Score);
                if (byScore != 0)
                {
                    return byScore;
                }

                return string.Compare(left.Symbol.Name, right.Symbol.Name, StringComparison.OrdinalIgnoreCase);
            });

            if (matches.Count > MaxResults)
            {
                matches.RemoveRange(MaxResults, matches.Count - MaxResults);
            }

            return [.. matches.Select(static item => item.Symbol)];
        }

        internal static int Score(string query, BaseSymbol symbol, bool qualified)
        {
            string name = symbol.Name ?? string.Empty;
            string fqn = (symbol.FullyQualifiedName ?? name).TrimStart('\\');
            if (string.IsNullOrEmpty(query))
            {
                return 1;
            }

            if (qualified)
            {
                return Math.Max(ScoreHaystack(query.TrimStart('\\'), fqn), ScoreHaystack(query, name));
            }

            int nameScore = ScoreHaystack(query, name);
            if (nameScore > 0)
            {
                return nameScore;
            }

            // Unqualified queries still match a fully-qualified name so
            // `Namespace\ClassName` fragments work without a leading slash.
            return fqn.Contains('\\', StringComparison.Ordinal)
                ? ScoreHaystack(query, fqn)
                : 0;
        }

        private static int ScoreHaystack(string query, string haystack)
        {
            if (string.IsNullOrEmpty(haystack))
            {
                return 0;
            }

            if (string.Equals(haystack, query, StringComparison.OrdinalIgnoreCase))
            {
                return 1000;
            }

            if (haystack.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return 800;
            }

            int substring = haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (substring >= 0)
            {
                return 600 - Math.Min(substring, 100);
            }

            return FuzzyScore(query, haystack);
        }

        private static int FuzzyScore(string query, string haystack)
        {
            int qi = 0;
            int consecutive = 0;
            int bonus = 0;
            for (int hi = 0; hi < haystack.Length && qi < query.Length; hi++)
            {
                if (char.ToLowerInvariant(haystack[hi]) != char.ToLowerInvariant(query[qi]))
                {
                    consecutive = 0;
                    continue;
                }

                consecutive++;
                bonus += consecutive > 1 ? 2 : 1;
                if (hi == 0 || haystack[hi - 1] == '\\' || char.IsUpper(haystack[hi]))
                {
                    bonus += 4;
                }

                qi++;
            }

            if (qi < query.Length)
            {
                return 0;
            }

            return 400 + bonus - haystack.Length;
        }

        private static bool IsWorkspaceSymbol(BaseSymbol symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol.Name)
                || symbol.Name.StartsWith("anonClass@", StringComparison.Ordinal)
                || symbol.Name.StartsWith("anonStruct@", StringComparison.Ordinal)
                || symbol.Name == "<error>")
            {
                return false;
            }

            if (symbol is ObjectDeclarationSymbol obj)
            {
                return !obj.IsCompilerGenerated
                    && !obj.IsExtension
                    && symbol is not AnonymousObjectDeclarationSymbol;
            }

            return symbol is FunctionDeclarationSymbol
                or ConstantSymbol
                or ObjectConstantSymbol
                or TypeAliasSymbol
                or ObjectTypeAliasSymbol;
        }

        private static SymbolInformation? ToInformation(BaseSymbol symbol, WorkspaceManager workspace)
        {
            Uri? uri = TyhpLanguageServer.ToSourceUri(symbol.SourceFile, workspace, requestUri: DummyRequestUri);
            if (uri is null || uri == DummyRequestUri)
            {
                return null;
            }

            if (string.IsNullOrEmpty(symbol.SourceFile)
                && symbol.DeclaringAstNode is null
                && symbol.Line < 1)
            {
                return null;
            }

            return new SymbolInformation
            {
                Name = symbol.Name,
                Kind = ToKind(symbol),
                ContainerName = ContainerName(symbol),
                Location = new Location
                {
                    Uri = uri,
                    Range = PositionUtilities.ToLspRange(symbol),
                },
            };
        }

        private static Uri DummyRequestUri { get; } = new("file:///tyhp-workspace-symbol");

        private static SymbolKind ToKind(BaseSymbol symbol)
        {
            if (symbol is ObjectDeclarationSymbol obj)
            {
                if (obj.IsStruct)
                {
                    return SymbolKind.Struct;
                }

                return obj.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => SymbolKind.Interface,
                    PhpTypeDeclType.Enum => SymbolKind.Enum,
                    PhpTypeDeclType.Trait => SymbolKind.Class,
                    _ => SymbolKind.Class,
                };
            }

            if (symbol is ObjectConstantSymbol { IsEnumCase: true })
            {
                return SymbolKind.EnumMember;
            }

            return symbol switch
            {
                FunctionDeclarationSymbol => SymbolKind.Function,
                ConstantSymbol or ObjectConstantSymbol => SymbolKind.Constant,
                TypeAliasSymbol or ObjectTypeAliasSymbol => SymbolKind.TypeParameter,
                _ => SymbolKind.Variable,
            };
        }

        private static string? ContainerName(BaseSymbol symbol)
        {
            IBaseScope? scope = symbol.ContainingScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj
                    && !string.IsNullOrEmpty(obj.Name)
                    && !string.Equals(obj.Name, symbol.Name, StringComparison.Ordinal))
                {
                    return obj.Name;
                }

                if (scope.DeclarationSymbol is NamespaceSymbol ns
                    && !string.IsNullOrEmpty(ns.Name))
                {
                    return ns.Name;
                }

                scope = scope.ParentScope;
            }

            string fqn = (symbol.FullyQualifiedName ?? string.Empty).TrimStart('\\');
            int last = fqn.LastIndexOf('\\');
            return last > 0 ? fqn[..last] : null;
        }
    }
}
