using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        /// <summary>
        /// Recursively expands type aliases until a non-alias type is reached.
        /// The <paramref name="resolveAliasBody"/> callback resolves an alias's underlying type expression.
        /// </summary>
        public static ICheckedType ExpandTypeAliases(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, ICheckedType> resolveAliasBody) =>
            ExpandTypeAliasesCore(type, symbolTree, globalScope, resolveAliasBody, new HashSet<IBaseSymbol>());

        private static ICheckedType ExpandTypeAliasesCore(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, ICheckedType> resolveAliasBody,
            HashSet<IBaseSymbol> visiting)
        {
            switch (type)
            {
                case SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol alias }:
                    return ExpandAliasSymbol(alias, [], symbolTree, globalScope, resolveAliasBody, visiting);

                case GenericCheckedType generic when generic.BaseType is SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol alias }:
                    return ExpandAliasSymbol(alias, generic.TypeArguments, symbolTree, globalScope, resolveAliasBody, visiting);

                case UnionCheckedType union:
                    return CheckedTypes.UnionTypes(
                        union.Members.Select(m => ExpandTypeAliasesCore(m, symbolTree, globalScope, resolveAliasBody, visiting)).ToList());

                case IntersectionCheckedType intersection:
                {
                    ICheckedType? result = null;
                    foreach (var member in intersection.Members)
                    {
                        var expanded = ExpandTypeAliasesCore(member, symbolTree, globalScope, resolveAliasBody, visiting);
                        result = result is null ? expanded : IntersectTypes(result, expanded, symbolTree, globalScope);
                    }

                    return result ?? CheckedTypes.Unresolved;
                }

                case NullableCheckedType nullable:
                {
                    var inner = ExpandTypeAliasesCore(nullable.InnerType, symbolTree, globalScope, resolveAliasBody, visiting);
                    return inner.IsNullable ? inner : new NullableCheckedType(inner);
                }

                default:
                    return type;
            }
        }

        private static ICheckedType ExpandAliasSymbol(
            TypeAliasSymbol alias,
            IReadOnlyList<ICheckedType> typeArguments,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, ICheckedType> resolveAliasBody,
            HashSet<IBaseSymbol> visiting)
        {
            if (!visiting.Add(alias))
            {
                return CheckedTypes.FromSymbol(alias);
            }

            if (alias.AliasedType is null)
            {
                visiting.Remove(alias);
                return CheckedTypes.FromSymbol(alias);
            }

            var resolved = resolveAliasBody(alias.AliasedType);

            if (typeArguments.Count > 0 && alias.GenericParameters.Count > 0)
            {
                var substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                for (var i = 0; i < Math.Min(alias.GenericParameters.Count, typeArguments.Count); i++)
                {
                    substitutions[alias.GenericParameters[i].Name] = typeArguments[i];
                }

                resolved = ResolveGenericType(resolved, substitutions, symbolTree, globalScope);
            }

            var expanded = ExpandTypeAliasesCore(resolved, symbolTree, globalScope, resolveAliasBody, visiting);
            visiting.Remove(alias);
            return expanded;
        }
    }
}
