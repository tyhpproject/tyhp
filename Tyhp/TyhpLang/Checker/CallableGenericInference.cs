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
                ParameterPackCheckedType p =>
                    (p.SourceCallable is not null && ContainsUnboundGeneric(p.SourceCallable))
                    || p.Members.Any(ContainsUnboundGeneric),
                HomogeneousVariadicCheckedType h => ContainsUnboundGeneric(h.ElementType),
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
                // `__ClassName<Foo>` is a branded string, not an object. Binding it to a naked
                // `T extends object` in `T|__ClassName<T>` is the wrong arm — the brand argument
                // (`Foo`) is filled by the structured `__ClassName<T>` member instead.
                if (!TypeComparer.IsUnresolvedType(actual)
                    && !TypeComparer.IsMixedType(actual)
                    && !bindings.ContainsKey(param)
                    && !SymbolNameTypeHelper.IsSymbolNameType(actual))
                {
                    bindings[param] = actual;
                }

                return;
            }

            // Callable-keyed utilities mention TCallable but the argument is a bag / return
            // value, not the callable. Binding TCallable from that actual would steal the
            // inference that should come from the callback argument. `__Properties<T>` is the
            // same shape: T comes from the object argument, not from the property bag.
            if (SymbolNameTypeHelper.TryGetUtilitySymbol(pattern, out var utility)
                && UtilityTypeResolver.IsCallSiteExpandingUtility(utility.Behavior))
            {
                return;
            }

            // `callable(TValue): TResult` / `\Closure<callable(...)>` vs a closure or callable
            // argument — unify parameter slots and the return-last result on the callable
            // facet (binds array_map's TResult). Closure class args are not return-last.
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

            if (pattern is GenericCheckedType unionPattern
                && actual is UnionCheckedType actualUnion)
            {
                foreach (var member in actualUnion.Members)
                {
                    CollectGenericBindings(unionPattern, member, bindings);
                }

                return;
            }

            if (pattern is GenericCheckedType patternGeneric
                && actual is GenericCheckedType actualGeneric
                && patternGeneric.TypeArguments.Count > 0
                && actualGeneric.TypeArguments.Count > 0
                && GenericTypeArgumentsAlign(pattern, actual))
            {
                // Align from the right so `array<TValue>` matches `array<K,V>`'s value slot
                // (single-arg shorthand vs full key/value form). `\Closure<T>` is the exception:
                // the callable shape is the first argument and the rest are defaulted
                // `$this` / scope parameters, so a 1-arg pattern matches the shape, not the scope.
                var patternArgs = patternGeneric.TypeArguments;
                var actualArgs = actualGeneric.TypeArguments;
                var closure = CallableArityFacetBuilder.IsClosureTypeName(patternGeneric.BaseType);
                var offset = closure ? 0 : Math.Max(0, actualArgs.Count - patternArgs.Count);
                var limit = closure ? Math.Min(patternArgs.Count, actualArgs.Count) : patternArgs.Count;
                for (var i = 0; i < limit && offset + i < actualArgs.Count; i++)
                {
                    CollectGenericBindings(patternArgs[i], actualArgs[offset + i], bindings);
                }
            }
            else if (TryBindClassNameBrandFromString(pattern, actual, bindings))
            {
                return;
            }
            else if (pattern is UnionCheckedType patternUnion)
            {
                BindUnionPattern(patternUnion, actual, bindings);
            }
        }

        /// <summary>
        /// Pairwise matching is only for all-naked unions (<c>T|U</c> vs <c>int|string</c>).
        /// Mixed shapes such as <c>T|__ClassName&lt;T&gt;</c> vs
        /// <c>__ClassName&lt;A&gt;|__ClassName&lt;B&gt;</c> must not pair <c>T</c> with a brand.
        /// Non-parameter arms run first so a branded <c>__ClassName&lt;Foo&gt;</c> fills
        /// <c>T</c> from the brand argument rather than binding <c>T = __ClassName&lt;Foo&gt;</c>.
        /// </summary>
        private static void BindUnionPattern(
            UnionCheckedType patternUnion,
            ICheckedType actual,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            if (actual is UnionCheckedType actualUnion
                && patternUnion.Members.Count == actualUnion.Members.Count
                && patternUnion.Members.All(IsNakedTypeParameter))
            {
                for (var i = 0; i < patternUnion.Members.Count; i++)
                {
                    CollectGenericBindings(patternUnion.Members[i], actualUnion.Members[i], bindings);
                }

                return;
            }

            var actualMembers = EnumerateUnionMembers(actual).ToList();
            foreach (var actualMember in actualMembers)
            {
                foreach (var member in patternUnion.Members)
                {
                    if (!IsNakedTypeParameter(member))
                    {
                        CollectGenericBindings(member, actualMember, bindings);
                    }
                }
            }

            foreach (var actualMember in actualMembers)
            {
                foreach (var member in patternUnion.Members)
                {
                    if (IsNakedTypeParameter(member))
                    {
                        CollectGenericBindings(member, actualMember, bindings);
                    }
                }
            }
        }

        private static IEnumerable<ICheckedType> EnumerateUnionMembers(ICheckedType type)
        {
            if (type is UnionCheckedType union)
            {
                foreach (var member in union.Members)
                {
                    foreach (var inner in EnumerateUnionMembers(member))
                    {
                        yield return inner;
                    }
                }

                yield break;
            }

            yield return type;
        }

        private static bool IsNakedTypeParameter(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol };
        }

        /// <summary>
        /// A class-name <c>string</c> matches <c>__ClassName&lt;T&gt;</c> as <c>T = object</c>
        /// (the brand's default). Without this, naked <c>T</c> in <c>T|__ClassName&lt;T&gt;</c>
        /// binds <c>T = string</c> and fails <c>T extends object</c>.
        /// </summary>
        private static bool TryBindClassNameBrandFromString(
            ICheckedType pattern,
            ICheckedType actual,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            if (pattern is not GenericCheckedType { TypeArguments.Count: > 0 } brand
                || !SymbolNameTypeHelper.TryGetBehavior(pattern, out var behavior)
                || !SymbolNameTypeHelper.IsOptionalSingleObjectBrand(behavior)
                || !IsStringType(actual))
            {
                return false;
            }

            CollectGenericBindings(
                brand.TypeArguments[0],
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object")),
                bindings);
            return true;
        }

        private static bool IsStringType(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol { Name: var name } }
                && string.Equals(name, "string", StringComparison.OrdinalIgnoreCase)
            || type is LiteralCheckedType { UnderlyingType: SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol { Name: var underlying } } }
                && string.Equals(underlying, "string", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// <c>__ClassName&lt;T&gt;</c> vs <c>Iterator&lt;K,V&gt;</c> are both generic but must
        /// not unify T from Iterator's type arguments. Same-utility brands still unify
        /// (<c>__ClassName&lt;T&gt;</c> vs <c>__ClassName&lt;Foo&gt;</c>). Non-utility generics
        /// (<c>Traversable</c> vs <c>Iterator</c>) keep the existing type-argument walk.
        /// </summary>
        private static bool GenericTypeArgumentsAlign(ICheckedType pattern, ICheckedType actual)
        {
            if (!SymbolNameTypeHelper.TryGetUtilitySymbol(pattern, out var patternUtility))
            {
                return true;
            }

            return SymbolNameTypeHelper.TryGetUtilitySymbol(actual, out var actualUtility)
                && actualUtility.Behavior == patternUtility.Behavior;
        }
    }
}
