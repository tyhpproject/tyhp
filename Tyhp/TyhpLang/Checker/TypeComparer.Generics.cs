using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        private static ICheckedType ResolveGenericTypeCore(
            ICheckedType generic,
            Dictionary<string, ICheckedType> typeArguments,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            SubstituteType(
                generic,
                param => typeArguments.TryGetValue(param.Name, out var byName) ? byName : null,
                symbolTree,
                globalScope);

        /// <summary>
        /// Substitutes type parameters matched by <em>symbol identity</em> rather than by name. Each
        /// declaration owns its own <see cref="GenericTypeParameterSymbol"/> instances, so a chain such
        /// as <c>Derived&lt;T&gt; extends Base&lt;T&gt;</c> — where both levels spell the parameter
        /// <c>T</c> but bind it to different arguments — stays unambiguous. See FOUND_BUGS.md item 11.
        /// </summary>
        internal static ICheckedType ResolveGenericTypeBySymbol(
            ICheckedType generic,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> typeArguments,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            SubstituteType(
                generic,
                param => typeArguments.TryGetValue(param, out var bySymbol) ? bySymbol : null,
                symbolTree,
                globalScope);

        private static ICheckedType SubstituteType(
            ICheckedType type,
            Func<GenericTypeParameterSymbol, ICheckedType?> lookup,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            SubstituteType(type, lookup, symbolTree, globalScope, substituting: null);

        /// <summary>
        /// <paramref name="substituting"/> holds the parameters on the current substitution path, so a
        /// binding that leads back to one already being substituted can be left alone instead of
        /// recurring without end. Parameters are popped as their subtree completes, which keeps the
        /// guard to the path rather than the whole traversal — two type arguments that each mention the
        /// same parameter must both still be substituted.
        /// </summary>
        private static ICheckedType SubstituteType(
            ICheckedType type,
            Func<GenericTypeParameterSymbol, ICheckedType?> lookup,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<GenericTypeParameterSymbol>? substituting)
        {
            if (type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol parameter } &&
                lookup(parameter) is { } substitution)
            {
                // Name-keyed lookups collide across declarations: a guard declared as
                // `isType<TTarget>` called from a class that also names a parameter `TTarget` maps
                // `TTarget` onto itself. Substitution still repeats, because a parameter may legitimately
                // resolve to another parameter (`T` -> `U` -> `int`).
                substituting ??= new HashSet<GenericTypeParameterSymbol>();

                if (!substituting.Add(parameter))
                {
                    return type;
                }

                try
                {
                    return SubstituteType(substitution, lookup, symbolTree, globalScope, substituting);
                }
                finally
                {
                    substituting.Remove(parameter);
                }
            }

            return type switch
            {
                SimpleCheckedType simple => simple,
                NullableCheckedType nullable =>
                    new NullableCheckedType(
                        SubstituteType(nullable.InnerType, lookup, symbolTree, globalScope, substituting)),
                UnionCheckedType union =>
                    UnionTypes(
                        union.Members
                            .Select(member =>
                                SubstituteType(member, lookup, symbolTree, globalScope, substituting))
                            .ToList(),
                        symbolTree,
                        globalScope),
                IntersectionCheckedType intersection =>
                    intersection.Members
                        .Select(member =>
                            SubstituteType(member, lookup, symbolTree, globalScope, substituting))
                        .Aggregate((accumulated, next) =>
                            IntersectTypes(accumulated, next, symbolTree, globalScope)),
                GenericCheckedType genericType => UtilityTypeResolver.ExpandAfterSubstitution(
                    // Deferred `__CallableReturnType<T>` / `__CallableParametersStruct<T>` /
                    // `__CallableParametersTuple<T>` / `__CallableParametersRest<T>` expand
                    // once T is bound (Rest keeps its wrapper so call-site unpack can see it).
                    new GenericCheckedType(
                        SubstituteType(genericType.BaseType, lookup, symbolTree, globalScope, substituting),
                        genericType.TypeArguments
                            .Select(arg => SubstituteType(arg, lookup, symbolTree, globalScope, substituting))
                            .ToList())),
                CallableCheckedType callable => callable.MapTypes(mapped =>
                    SubstituteType(mapped, lookup, symbolTree, globalScope, substituting)),
                StructCheckedType structType => new StructCheckedType(
                    structType.Properties.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.WithType(
                            SubstituteType(pair.Value.Type, lookup, symbolTree, globalScope, substituting)))),
                LiteralCheckedType literal => new LiteralCheckedType(
                    literal.Value,
                    (SimpleCheckedType)SubstituteType(
                        literal.UnderlyingType, lookup, symbolTree, globalScope, substituting)),
                StaticCheckedType staticType => new StaticCheckedType(
                    SubstituteType(staticType.DeclaringType, lookup, symbolTree, globalScope, substituting)),
                _ => type,
            };
        }
    }
}
