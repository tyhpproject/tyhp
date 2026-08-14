using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Builds <see cref="CallableCheckedType"/> arity facets from a parameter list with defaults,
    /// and selects a facet from an intersection of callables by argument count.
    /// </summary>
    internal static class CallableArityFacetBuilder
    {
        /// <summary>
        /// Builds a single <see cref="CallableCheckedType"/> or an
        /// <see cref="IntersectionCheckedType"/> of arity siblings when trailing parameters have
        /// defaults. A trailing variadic contributes one extra facet rather than an unbounded
        /// family.
        /// </summary>
        public static ICheckedType Build(
            IReadOnlyList<ICheckedType> parameterTypes,
            IReadOnlyList<(bool HasDefault, bool IsVariadic)> parameterFlags,
            ICheckedType returnType,
            IReadOnlyList<string?>? parameterNames = null)
        {
            if (parameterTypes.Count != parameterFlags.Count)
            {
                throw new ArgumentException(
                    "Parameter types and flags must have the same length.",
                    nameof(parameterFlags));
            }

            var nonVariadicTypes = new List<ICheckedType>(parameterTypes.Count);
            var nonVariadicNames = parameterNames is null ? null : new List<string?>(parameterTypes.Count);
            ICheckedType? variadicType = null;
            string? variadicName = null;
            for (var i = 0; i < parameterTypes.Count; i++)
            {
                var name = parameterNames is not null && i < parameterNames.Count
                    ? parameterNames[i]
                    : null;
                if (parameterFlags[i].IsVariadic)
                {
                    variadicType ??= parameterTypes[i];
                    variadicName ??= name;
                }
                else
                {
                    nonVariadicTypes.Add(parameterTypes[i]);
                    nonVariadicNames?.Add(name);
                }
            }

            var prefixes = ArityFacetExpansion.GetValidArityPrefixes(parameterFlags);
            var facets = new List<ICheckedType>(prefixes.Count + 1);
            foreach (var arity in prefixes)
            {
                facets.Add(new CallableCheckedType(
                    nonVariadicTypes.Take(arity).ToList(),
                    returnType,
                    nonVariadicNames?.Take(arity).ToList()));
            }

            // A trailing `...$args` accepts any number of extra arguments. Facets cannot be
            // unbounded, so model the single-extra case: `f(T ...$xs)` still matches a
            // `callable<T, R>` target, while higher arities stay unconstrained rather than
            // exploding into infinite siblings.
            if (variadicType is not null)
            {
                var withVariadic = new List<ICheckedType>(nonVariadicTypes) { variadicType };
                IReadOnlyList<string?>? withVariadicNames = null;
                if (nonVariadicNames is not null)
                {
                    withVariadicNames = [.. nonVariadicNames, variadicName];
                }

                facets.Add(new CallableCheckedType(
                    withVariadic, returnType, withVariadicNames, lastParameterIsVariadic: true));
            }

            return FlattenCallableIntersection(facets);
        }

        /// <summary>
        /// Convenience overload for binder <see cref="ParameterInfo"/> lists.
        /// </summary>
        public static ICheckedType BuildFromParameterInfos(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            ICheckedType returnType)
        {
            var flags = parameters
                .Select(p => (HasDefault: p.DefaultValue is not null, p.IsVariadic))
                .ToList();
            var names = parameters
                .Select(p => CallableSignatureReflection.NormalizeParameterName(p.Name))
                .ToList();
            return Build(parameterTypes, flags, returnType, names);
        }

        /// <summary>
        /// Convenience overload for closure/inline-function AST parameters.
        /// </summary>
        public static ICheckedType BuildFromClosureParameters(
            IReadOnlyList<PhpParameterAst> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            ICheckedType returnType)
        {
            var flags = parameters
                .Select(p => (HasDefault: p.DefaultValue is not null, p.IsVariadic))
                .ToList();
            var names = parameters
                .Select(p => CallableSignatureReflection.NormalizeParameterName(p.Name))
                .ToList();
            return Build(parameterTypes, flags, returnType, names);
        }

        /// <summary>
        /// Collects every <see cref="CallableCheckedType"/> in <paramref name="type"/>,
        /// unwrapping nullables and flattening intersections.
        /// </summary>
        public static IReadOnlyList<CallableCheckedType> GetCallableFacets(ICheckedType type)
        {
            var results = new List<CallableCheckedType>();
            CollectCallableFacets(type, results);
            return results;
        }

        /// <summary>
        /// Selects the callable facet whose parameter arity matches
        /// <paramref name="argumentCount"/>. Returns false when no facet matches.
        /// </summary>
        public static bool TrySelectCallableFacet(
            ICheckedType type,
            int argumentCount,
            out CallableCheckedType? facet)
        {
            facet = null;
            foreach (var candidate in GetCallableFacets(type))
            {
                if (candidate.ParameterTypes.Count == argumentCount)
                {
                    facet = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Selects a facet for contextual closure typing: prefer exact arity match to
        /// <paramref name="closureParameterCount"/>; otherwise the longest facet whose arity is
        /// ≤ that count.
        /// </summary>
        public static bool TrySelectCallableFacetForClosure(
            ICheckedType type,
            int closureParameterCount,
            out CallableCheckedType? facet)
        {
            facet = null;
            var facets = GetCallableFacets(type);
            if (facets.Count == 0)
            {
                return false;
            }

            foreach (var candidate in facets)
            {
                if (candidate.ParameterTypes.Count == closureParameterCount)
                {
                    facet = candidate;
                    return true;
                }
            }

            CallableCheckedType? best = null;
            foreach (var candidate in facets)
            {
                if (candidate.ParameterTypes.Count <= closureParameterCount
                    && (best is null
                        || candidate.ParameterTypes.Count > best.ParameterTypes.Count))
                {
                    best = candidate;
                }
            }

            if (best is null)
            {
                return false;
            }

            facet = best;
            return true;
        }

        /// <summary>
        /// True when <paramref name="type"/> is (or unwraps to) one or more callable facets.
        /// </summary>
        public static bool IsCallableFacetType(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type switch
            {
                CallableCheckedType => true,
                IntersectionCheckedType intersection =>
                    intersection.Members.Any(IsCallableFacetType),
                GenericCheckedType { TypeArguments.Count: > 0 } generic =>
                    Rules.CheckerHelpers.IsBuiltInName(generic.BaseType, "callable")
                    || IsClosureTypeName(generic.BaseType),
                _ => Rules.CheckerHelpers.IsBuiltInName(type, "callable"),
            };
        }

        /// <summary>
        /// Counts the positional (non-named, non-unpacked) arguments of a call — the arity used to
        /// pick a facet out of an optional-arity intersection.
        /// </summary>
        public static int CountPositionalArguments(PhpArgumentListAst? arguments)
        {
            if (arguments is null)
            {
                return 0;
            }

            var count = 0;
            foreach (var argument in arguments.GetAllNotNull())
            {
                if (!argument.IsVariadic && argument.Name is null)
                {
                    count++;
                }
            }

            return count;
        }

        private static void CollectCallableFacets(ICheckedType type, List<CallableCheckedType> results)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            switch (type)
            {
                case CallableCheckedType callable:
                    results.Add(callable);
                    break;
                case IntersectionCheckedType intersection:
                    foreach (var member in intersection.Members)
                    {
                        CollectCallableFacets(member, results);
                    }

                    break;
                case GenericCheckedType { TypeArguments.Count: > 0 } generic
                    when Rules.CheckerHelpers.IsBuiltInName(generic.BaseType, "callable")
                        || IsClosureTypeName(generic.BaseType):
                    results.Add(new CallableCheckedType(
                        generic.TypeArguments.Take(generic.TypeArguments.Count - 1).ToList(),
                        generic.TypeArguments[^1]));
                    break;
                case SimpleCheckedType simple
                    when Rules.CheckerHelpers.IsBuiltInName(simple, "callable"):
                    results.Add(new CallableCheckedType([], CheckedTypes.Mixed));
                    break;
            }
        }

        /// <summary>
        /// True for the nominal <c>\Closure</c> class (bare or as a generic base). Shared with
        /// intersection-member validation so both sides agree on what counts as callable-like.
        /// </summary>
        public static bool IsClosureTypeName(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol.Name: "Closure" }
            || string.Equals(type.DisplayName, "Closure", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.DisplayName, "\\Closure", StringComparison.OrdinalIgnoreCase);

        private static ICheckedType FlattenCallableIntersection(IReadOnlyList<ICheckedType> facets)
        {
            var flattened = new List<ICheckedType>();
            foreach (var facet in facets)
            {
                if (facet is IntersectionCheckedType nested)
                {
                    flattened.AddRange(nested.Members);
                }
                else
                {
                    flattened.Add(facet);
                }
            }

            var distinct = new List<ICheckedType>();
            foreach (var member in flattened)
            {
                if (!distinct.Any(existing => CheckedTypes.AreTypesEqual(existing, member)))
                {
                    distinct.Add(member);
                }
            }

            return distinct.Count switch
            {
                0 => new CallableCheckedType([], CheckedTypes.Mixed),
                1 => distinct[0],
                _ => new IntersectionCheckedType(distinct),
            };
        }
    }
}
