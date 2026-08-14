using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Argument-driven generic binding for callable <em>values</em> (first-class callables,
    /// closure-typed variables, <c>|&gt;</c> RHS). Direct named calls bind via
    /// <c>TryInferGenericBindings</c> against the callee symbol; this path recovers the same
    /// structural matching from a <see cref="CallableCheckedType"/> facet whose parameter/return
    /// types still mention unbound <see cref="GenericTypeParameterSymbol"/>s.
    /// </summary>
    internal static class CallableGenericInference
    {
        /// <summary>
        /// True when <paramref name="type"/> still mentions any
        /// <see cref="GenericTypeParameterSymbol"/> (open generic left after acquiring
        /// <c>keep_keys(...)</c> / similar).
        /// </summary>
        public static bool ContainsUnboundGeneric(ICheckedType type) =>
            type switch
            {
                SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol } => true,
                NullableCheckedType n => ContainsUnboundGeneric(n.InnerType),
                GenericCheckedType g =>
                    ContainsUnboundGeneric(g.BaseType)
                    || g.TypeArguments.Any(ContainsUnboundGeneric),
                UnionCheckedType u => u.Members.Any(ContainsUnboundGeneric),
                IntersectionCheckedType i => i.Members.Any(ContainsUnboundGeneric),
                CallableCheckedType c =>
                    ContainsUnboundGeneric(c.ReturnType)
                    || c.ParameterTypes.Any(ContainsUnboundGeneric),
                StructCheckedType s => s.Properties.Values.Any(p => ContainsUnboundGeneric(p.Type)),
                _ => false,
            };

        /// <summary>
        /// True when the facet still has open generics that argument-driven inference may fill.
        /// </summary>
        public static bool FacetNeedsArgumentInference(CallableCheckedType facet) =>
            ContainsUnboundGeneric(facet.ReturnType)
            || facet.ParameterTypes.Any(ContainsUnboundGeneric);

        /// <summary>
        /// Infers type-argument bindings by structurally matching each facet parameter type
        /// against the corresponding positional argument type (same rules as direct-call
        /// <c>CollectGenericBindings</c>).
        /// </summary>
        public static bool TryInferFacetBindings(
            CallableCheckedType facet,
            IReadOnlyList<ICheckedType> positionalArgumentTypes,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            if (positionalArgumentTypes.Count == 0 || facet.ParameterTypes.Count == 0)
            {
                return false;
            }

            var count = Math.Min(facet.ParameterTypes.Count, positionalArgumentTypes.Count);
            for (var i = 0; i < count; i++)
            {
                CollectGenericBindings(
                    facet.ParameterTypes[i],
                    positionalArgumentTypes[i],
                    bindings);
            }

            return bindings.Count > 0;
        }

        /// <summary>
        /// Substitutes inferred bindings into every parameter slot and the return type of
        /// <paramref name="facet"/>.
        /// </summary>
        public static CallableCheckedType SubstituteFacet(
            CallableCheckedType facet,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (bindings.Count == 0)
            {
                return facet;
            }

            return facet.MapTypes(t => TypeComparer.ResolveGenericTypeBySymbol(
                t, bindings, symbolTree, globalScope));
        }

        /// <summary>
        /// Structural match of a declared (pattern) type against an actual type, recording the
        /// first binding for each <see cref="GenericTypeParameterSymbol"/>. Shared by direct-call
        /// inference and callable-facet invocation.
        /// </summary>
        public static void CollectGenericBindings(
            ICheckedType pattern,
            ICheckedType actual,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            while (pattern is NullableCheckedType np)
            {
                pattern = np.InnerType;
            }

            while (actual is NullableCheckedType na)
            {
                actual = na.InnerType;
            }

            if (pattern is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol param })
            {
                if (!TypeComparer.IsUnresolvedType(actual)
                    && !TypeComparer.IsMixedType(actual)
                    && !bindings.ContainsKey(param))
                {
                    bindings[param] = actual;
                }

                return;
            }

            // Callable-keyed utilities mention TCallable but the argument is a bag / return
            // value, not the callable. Binding TCallable from that actual would steal the
            // inference that should come from the callback argument.
            if (SymbolNameTypeHelper.TryGetUtilitySymbol(pattern, out var utility)
                && utility.Behavior is UtilityBehavior.CallableParametersStruct
                    or UtilityBehavior.CallableParametersTuple
                    or UtilityBehavior.CallableParametersRest
                    or UtilityBehavior.CallableReturnType
                    or UtilityBehavior.ReturnType)
            {
                return;
            }

            // `callable<TValue, TResult>` / `\Closure<…>` vs a closure or callable argument —
            // unify parameter slots and the return-last result (binds array_map's TResult).
            var patternCallables = CallableArityFacetBuilder.GetCallableFacets(pattern);
            var actualCallables = CallableArityFacetBuilder.GetCallableFacets(actual);
            if (patternCallables.Count > 0 && actualCallables.Count > 0)
            {
                var patternFacet = patternCallables[0];
                if (!CallableArityFacetBuilder.TrySelectCallableFacetForClosure(
                        actual,
                        patternFacet.ParameterTypes.Count,
                        out var actualFacet)
                    || actualFacet is null)
                {
                    actualFacet = actualCallables[0];
                }

                var sharedArity = Math.Min(
                    patternFacet.ParameterTypes.Count,
                    actualFacet.ParameterTypes.Count);
                for (var i = 0; i < sharedArity; i++)
                {
                    CollectGenericBindings(
                        patternFacet.ParameterTypes[i],
                        actualFacet.ParameterTypes[i],
                        bindings);
                }

                CollectGenericBindings(patternFacet.ReturnType, actualFacet.ReturnType, bindings);
                return;
            }

            if (pattern is GenericCheckedType patternGeneric
                && actual is GenericCheckedType actualGeneric
                && patternGeneric.TypeArguments.Count > 0
                && actualGeneric.TypeArguments.Count > 0)
            {
                // Align from the right so `array<TValue>` matches `array<K,V>`'s value slot
                // (single-arg shorthand vs full key/value form).
                var patternArgs = patternGeneric.TypeArguments;
                var actualArgs = actualGeneric.TypeArguments;
                var offset = Math.Max(0, actualArgs.Count - patternArgs.Count);
                for (var i = 0; i < patternArgs.Count && offset + i < actualArgs.Count; i++)
                {
                    CollectGenericBindings(patternArgs[i], actualArgs[offset + i], bindings);
                }
            }
            else if (pattern is UnionCheckedType patternUnion && actual is UnionCheckedType actualUnion
                     && patternUnion.Members.Count == actualUnion.Members.Count)
            {
                for (var i = 0; i < patternUnion.Members.Count; i++)
                {
                    CollectGenericBindings(patternUnion.Members[i], actualUnion.Members[i], bindings);
                }
            }
        }
    }
}
