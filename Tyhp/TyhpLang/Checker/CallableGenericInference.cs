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
        /// Returns the type arguments <paramref name="target"/> receives from the nominal
        /// <c>actual</c> type through its inheritance chain, or null when it is not an ancestor.
        /// </summary>
        internal delegate IReadOnlyList<ICheckedType>? AncestorProjection(
            ICheckedType actual,
            ObjectDeclarationSymbol target);

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
        /// <param name="projectToAncestor">
        /// When supplied, a nominal <paramref name="actual"/> that is not the pattern's generic
        /// class/interface is first projected onto that ancestor (the type arguments it receives
        /// through its <c>extends</c> / <c>implements</c> chain), so <c>Task&lt;T&gt;</c> binds
        /// <c>T = int</c> from a <c>class CountTask implements Task&lt;int&gt;</c> argument.
        /// </param>
        public static void CollectGenericBindings(
            ICheckedType pattern,
            ICheckedType actual,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            AncestorProjection? projectToAncestor = null,
            bool mergeTypeArguments = false)
        {
            var patternIsNullable = false;
            while (pattern is NullableCheckedType np)
            {
                patternIsNullable = true;
                pattern = np.InnerType;
            }

            // A bare `T` receiving a nullable argument binds the nullable type (`T = ?int`), so
            // nullability is neither lost in the result nor in `T extends X` checks. `?T` already
            // accounts for the null, so there `T` binds the non-null part.
            var nullableActual = actual;
            while (actual is NullableCheckedType na)
            {
                actual = na.InnerType;
            }

            if (pattern is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol param })
            {
                if (!patternIsNullable)
                {
                    actual = nullableActual;
                }

                // `__ClassName<Foo>` is a branded string, not an object. Binding it to a naked
                // `T extends object` in `T|__ClassName<T>` is the wrong arm — the brand argument
                // (`Foo`) is filled by the structured `__ClassName<T>` member instead.
                // A `mixed` argument binds `T = mixed` (so `T extends X` is checked against it and
                // the result stays `mixed`); only the error-recovery `Unresolved` type is skipped.
                if (!TypeComparer.IsUnresolvedType(actual)
                    && !SymbolNameTypeHelper.IsSymbolNameType(actual))
                {
                    // A literal argument infers the widened type (`'plain'` → `string`,
                    // `1` → `int`) so `boxOf('plain')` is `Box<string>`.
                    var widened = GeneratorBodyInference.WidenLiteral(actual);
                    if (!bindings.TryGetValue(param, out var existing))
                    {
                        bindings[param] = widened;
                    }
                    else if (mergeTypeArguments
                        && !TypeComparer.AreTypesEqual(existing, widened))
                    {
                        // Array elements and variadic arguments contribute together:
                        // `Cell<int>` and `Cell<string>` bind `T` to `int|string`.
                        bindings[param] = CheckedTypes.UnionTypes(existing, widened);
                    }
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
                    bindings,
                    projectToAncestor,
                    mergeTypeArguments);
                }

                CollectGenericBindings(patternFacet.ReturnType, actualFacet.ReturnType, bindings, projectToAncestor, mergeTypeArguments);
                return;
            }

            if (pattern is GenericCheckedType unionPattern
                && actual is UnionCheckedType actualUnion)
            {
                foreach (var member in actualUnion.Members)
                {
                    // Each member of `Cell<int>|Cell<string>` contributes its type
                    // argument. Later members merge with the first instead of being dropped.
                    CollectGenericBindings(
                        unionPattern, member, bindings, projectToAncestor, mergeTypeArguments: true);
                }

                return;
            }

            // `Task<T>` against a class that merely implements/extends `Task<int>` (or a generic
            // class that forwards its own parameter): view the actual through the pattern's class.
            if (projectToAncestor is not null
                && pattern is GenericCheckedType
                {
                    TypeArguments.Count: > 0,
                    BaseType: SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol { GenericParameters.Count: > 0 } patternDecl } patternBase,
                }
                && TypeComparer.TryGetNominalSymbol(actual) is ObjectDeclarationSymbol actualDecl
                && !TypeComparer.SymbolsMatch(actualDecl, patternDecl)
                && projectToAncestor(actual, patternDecl) is { Count: > 0 } projected)
            {
                actual = new GenericCheckedType(patternBase, projected);
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
                    CollectGenericBindings(patternArgs[i], actualArgs[offset + i], bindings, projectToAncestor, mergeTypeArguments);
                }
            }
            else if (TryBindClassNameBrandFromString(pattern, actual, bindings))
            {
                return;
            }
            else if (pattern is UnionCheckedType patternUnion)
            {
                BindUnionPattern(patternUnion, actual, bindings, projectToAncestor, mergeTypeArguments);
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
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            AncestorProjection? projectToAncestor,
            bool mergeTypeArguments)
        {
            if (actual is UnionCheckedType actualUnion
                && patternUnion.Members.Count == actualUnion.Members.Count
                && patternUnion.Members.All(IsNakedTypeParameter))
            {
                for (var i = 0; i < patternUnion.Members.Count; i++)
                {
                    CollectGenericBindings(patternUnion.Members[i], actualUnion.Members[i], bindings, projectToAncestor, mergeTypeArguments);
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
                        CollectGenericBindings(member, actualMember, bindings, projectToAncestor);
                    }
                }
            }

            foreach (var actualMember in actualMembers)
            {
                foreach (var member in patternUnion.Members)
                {
                    if (IsNakedTypeParameter(member))
                    {
                        CollectGenericBindings(member, actualMember, bindings, projectToAncestor);
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
