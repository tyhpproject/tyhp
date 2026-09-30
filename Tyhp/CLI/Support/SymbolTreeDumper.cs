using System.Text.Json.Nodes;
using Tyhp.Domain.Diagnostics;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.CLI.Support
{
    /// <summary>
    /// Serializes the bound <see cref="GlobalScope"/> for the <c>symbol_tree</c> debug command.
    /// The dump is the same environment bind/check/emit use: builtins, package tyhpdefs,
    /// overlays, and project sources.
    /// </summary>
    public static class SymbolTreeDumper
    {
        /// <summary>
        /// Builds the JSON document for a bound compilation. Missing scope still yields a
        /// well-formed document so callers can inspect diagnostics after a failed bind.
        /// </summary>
        /// <param name="globalScope">Bound global scope, or null when bind did not run.</param>
        /// <param name="diagnostics">Parse/bind diagnostics to include alongside the tree.</param>
        /// <param name="nameFilter">Optional case-insensitive substring on Tyhp name, FQN, or PHP name.</param>
        /// <param name="phpVersion">Compile-target PHP version used for this bind.</param>
        /// <param name="sourceFileCount">Project source files parsed (tyhpdefs are extra).</param>
        public static JsonObject Dump(
            GlobalScope? globalScope,
            DiagnosticBag diagnostics,
            string? nameFilter,
            string phpVersion,
            int sourceFileCount)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);

            var root = new JsonObject
            {
                ["command"] = "symbol_tree",
                ["phpVersion"] = phpVersion,
                ["sourceFileCount"] = sourceFileCount,
            };

            if (globalScope is null)
            {
                root["counts"] = new JsonObject
                {
                    ["namespaces"] = 0,
                    ["symbols"] = 0,
                    ["types"] = 0,
                    ["functions"] = 0,
                };
                root["namespaces"] = new JsonArray();
                root["diagnostics"] = DebugJson.SerializeDiagnostics(diagnostics);
                return root;
            }

            var collected = CollectDeclarations(globalScope);
            var namespaces = BuildNamespaceGroups(collected, nameFilter);

            int symbolCount = 0;
            int typeCount = 0;
            int functionCount = 0;
            foreach (var group in namespaces)
            {
                foreach (var symbol in group.Symbols)
                {
                    symbolCount++;
                    if (symbol.Symbol is ObjectDeclarationSymbol or AnonymousObjectDeclarationSymbol
                        or BuiltInTypeSymbol or BuiltInUtilityTypeSymbol or TypeAliasSymbol)
                    {
                        typeCount++;
                    }
                    else if (symbol.Symbol is FunctionDeclarationSymbol or BuiltInFunctionSymbol)
                    {
                        functionCount++;
                    }
                }
            }

            root["counts"] = new JsonObject
            {
                ["namespaces"] = namespaces.Count,
                ["symbols"] = symbolCount,
                ["types"] = typeCount,
                ["functions"] = functionCount,
            };

            var namespacesArray = new JsonArray();
            foreach (var group in namespaces)
            {
                var symbolsArray = new JsonArray();
                foreach (var entry in group.Symbols)
                {
                    symbolsArray.Add(SerializeDeclaration(entry.Symbol, entry.Members));
                }

                namespacesArray.Add(new JsonObject
                {
                    ["name"] = group.Name,
                    ["symbols"] = symbolsArray,
                });
            }

            root["namespaces"] = namespacesArray;
            root["diagnostics"] = DebugJson.SerializeDiagnostics(diagnostics);
            return root;
        }

        private static List<CollectedSymbol> CollectDeclarations(GlobalScope globalScope)
        {
            var seen = new HashSet<IBaseSymbol>(ReferenceEqualityComparer.Instance);
            var collected = new List<CollectedSymbol>();

            Walk(globalScope, symbol =>
            {
                if (symbol is not BaseSymbol baseSymbol || !seen.Add(symbol))
                {
                    return;
                }

                if (IsMemberSymbol(baseSymbol) || IsStructuralNoise(baseSymbol))
                {
                    return;
                }

                collected.Add(new CollectedSymbol(baseSymbol));
            });

            return collected;
        }

        private static List<NamespaceGroup> BuildNamespaceGroups(
            List<CollectedSymbol> collected,
            string? nameFilter)
        {
            var filter = string.IsNullOrWhiteSpace(nameFilter) ? null : nameFilter.Trim();
            var groups = new Dictionary<string, List<FilteredDeclaration>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in collected)
            {
                IReadOnlyList<BaseSymbol>? memberSubset = null;
                if (item.Symbol is ObjectDeclarationSymbol type)
                {
                    var members = type.EnumerateMembersAndConstants().OfType<BaseSymbol>().ToList();
                    if (filter != null)
                    {
                        bool typeMatches = NameMatches(item.Symbol, filter);
                        var matchingMembers = typeMatches
                            ? members
                            : members.Where(member => NameMatches(member, filter)).ToList();
                        if (!typeMatches && matchingMembers.Count == 0)
                        {
                            continue;
                        }

                        memberSubset = matchingMembers;
                    }
                    else
                    {
                        memberSubset = members;
                    }
                }
                else if (filter != null && !NameMatches(item.Symbol, filter))
                {
                    continue;
                }

                string ns = NamespaceOf(item.Symbol);
                if (!groups.TryGetValue(ns, out var list))
                {
                    list = [];
                    groups[ns] = list;
                }

                list.Add(new FilteredDeclaration(item.Symbol, memberSubset));
            }

            var result = new List<NamespaceGroup>(groups.Count);
            foreach (var pair in groups.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var symbols = pair.Value
                    .OrderBy(d => KindSortKey(d.Symbol), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.Symbol.FullyQualifiedName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                result.Add(new NamespaceGroup(pair.Key, symbols));
            }

            return result;
        }

        private static JsonObject SerializeDeclaration(BaseSymbol symbol, IReadOnlyList<BaseSymbol>? members)
        {
            var obj = SerializeSymbolCore(symbol);

            if (symbol is FunctionDeclarationSymbol function && function.Overloads.Count > 0)
            {
                var overloads = new JsonArray();
                foreach (var overload in function.Overloads)
                {
                    overloads.Add(SerializeSymbolCore(overload));
                }

                obj["overloads"] = overloads;
            }

            if (symbol is ObjectDeclarationSymbol type)
            {
                obj["objectKind"] = type.ObjectKind.ToString();
                if (type.IsStruct)
                {
                    obj["isStruct"] = true;
                }

                if (type.IsExtension)
                {
                    obj["isExtension"] = true;
                }

                if (type.IsExtern)
                {
                    obj["isExtern"] = true;
                    if (!string.IsNullOrEmpty(type.ProvidedBy))
                    {
                        obj["providedBy"] = type.ProvidedBy;
                    }
                }

                if (type.IsCompilerGenerated)
                {
                    obj["isCompilerGenerated"] = true;
                }

                var memberList = members ?? type.EnumerateMembersAndConstants().OfType<BaseSymbol>().ToList();
                if (memberList.Count > 0)
                {
                    var membersArray = new JsonArray();
                    foreach (var member in memberList
                        .OrderBy(m => KindSortKey(m), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        membersArray.Add(SerializeSymbolCore(member));
                    }

                    obj["members"] = membersArray;
                }
            }

            return obj;
        }

        private static JsonObject SerializeSymbolCore(BaseSymbol symbol)
        {
            var obj = new JsonObject
            {
                ["kind"] = SymbolFormatter.KindLabel(symbol),
                ["symbolType"] = symbol.SymbolType.ToString(),
                ["name"] = symbol.Name,
                ["fqn"] = symbol.FullyQualifiedName,
                ["signature"] = SymbolFormatter.FormatSignature(symbol),
            };

            string? phpName = GetOriginalPhpName(symbol);
            if (!string.IsNullOrEmpty(phpName))
            {
                obj["phpName"] = phpName;
                if (!string.Equals(phpName, symbol.Name, StringComparison.Ordinal))
                {
                    obj["aliased"] = true;
                }
            }

            if (!string.IsNullOrEmpty(symbol.SourceFile))
            {
                obj["file"] = symbol.SourceFile;
            }

            if (symbol.Line > 0)
            {
                obj["line"] = symbol.Line;
                obj["column"] = symbol.Column;
            }

            if (symbol.IsDeprecated)
            {
                obj["deprecated"] = true;
            }

            if (symbol.IsObsolete)
            {
                obj["obsolete"] = true;
            }

            if (symbol.EffectivePhpVersionConstraints.Count > 0)
            {
                var constraints = new JsonArray();
                foreach (var constraint in symbol.EffectivePhpVersionConstraints)
                {
                    constraints.Add(constraint);
                }

                obj["phpConstraints"] = constraints;
            }

            var attributes = AttributeNames(symbol.DeclaringAstNode);
            if (attributes.Count > 0)
            {
                var attrArray = new JsonArray();
                foreach (var name in attributes)
                {
                    attrArray.Add(name);
                }

                obj["attributes"] = attrArray;
            }

            return obj;
        }

        private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                visit(symbol);
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child, visit);
            }
        }

        private static bool IsMemberSymbol(BaseSymbol symbol)
            => symbol is ObjectMethodSymbol
                or ObjectPropertySymbol
                or ObjectConstantSymbol
                or ObjectTypeAliasSymbol;

        private static bool IsStructuralNoise(BaseSymbol symbol)
            => symbol is FileSymbol
                or NoSymbol
                or NamespaceSymbol
                or NamespaceBlockSymbol
                or UseIncludeSymbol
                or VariableSymbol
                or GenericTypeParameterSymbol
                or LabelSymbol
                or CodeBlockSymbol
                or DeclareBlockSymbol
                or AnonymousFunctionSymbol;

        private static bool NameMatches(BaseSymbol symbol, string filter)
        {
            if (symbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (symbol.FullyQualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string? phpName = GetOriginalPhpName(symbol);
            return phpName != null && phpName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetOriginalPhpName(BaseSymbol symbol)
            => symbol switch
            {
                FunctionDeclarationSymbol { OriginalPhpName: { Length: > 0 } php } => php,
                ObjectDeclarationSymbol { OriginalPhpName: { Length: > 0 } php } => php,
                ObjectMethodSymbol { OriginalPhpName: { Length: > 0 } php } => php,
                _ => null,
            };

        private static string NamespaceOf(BaseSymbol symbol)
        {
            var fqn = symbol.FullyQualifiedName;
            if (string.IsNullOrEmpty(fqn))
            {
                return string.Empty;
            }

            fqn = fqn.TrimStart('\\');
            var slash = fqn.LastIndexOf('\\');
            return slash <= 0 ? string.Empty : fqn[..slash];
        }

        private static string KindSortKey(BaseSymbol symbol)
            => SymbolFormatter.KindLabel(symbol);

        private static List<string> AttributeNames(IBase2Ast? node)
        {
            if (node == null)
            {
                return [];
            }

            return node.AstAttributes
                .OfType<PhpAttributeAst>()
                .Select(attribute =>
                {
                    var written = attribute.Name?.ValueString ?? attribute.Name?.Identifier ?? "";
                    return written.Trim().TrimStart('\\');
                })
                .Where(static name => name.Length > 0)
                .ToList();
        }

        private sealed class CollectedSymbol(BaseSymbol symbol)
        {
            public BaseSymbol Symbol { get; } = symbol;
        }

        private sealed class FilteredDeclaration(BaseSymbol symbol, IReadOnlyList<BaseSymbol>? members)
        {
            public BaseSymbol Symbol { get; } = symbol;
            public IReadOnlyList<BaseSymbol>? Members { get; } = members;
        }

        private sealed class NamespaceGroup(string name, List<FilteredDeclaration> symbols)
        {
            public string Name { get; } = name;
            public List<FilteredDeclaration> Symbols { get; } = symbols;
        }
    }
}
