using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

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
        /// defaults. A trailing homogeneous variadic contributes one extra facet rather than an
        /// unbounded family. A trailing pack variadic (<c>Rest&lt;T&gt; ...$args</c>) splices T's
        /// parameters instead — it is not a 0-arg sibling plus a Rest-wrapper facet.
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

            // A pack variadic (`Rest<T> ...$args`) is value-position unpack of T's parameters,
            // not a homogeneous PHP variadic. It is the implementation of spliced
            // `callable(Rest<T> ...): R` — do not add a 0-arg sibling or a `Rest...` wrapper facet.
            if (variadicType is not null && ParameterPack.IsPack(variadicType))
            {
                var combined = new List<ICheckedType>(nonVariadicTypes) { variadicType };
                var spliced = ParameterPack.SpliceParameterList(combined, out var packVariadic);
                return new CallableCheckedType(
                    spliced,
                    returnType,
                    lastParameterIsVariadic: packVariadic);
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
            // `callable(T): R` target, while higher arities stay unconstrained rather than
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
        /// Builds arity facets from a parsed <c>callable(…): R</c> shape. Optional parameters
        /// (<c>=</c>) produce prefix siblings; a written default expression is ignored.
        /// <c>callable(...): R</c> is the constraint-only any-arity facet.
        /// </summary>
        public static ICheckedType BuildFromShape(
            TyhpCallableShapeAst shape,
            Func<ITypeExpression, bool, ICheckedType> resolveType)
        {
            var returnType = shape.ReturnType is null
                ? CheckedTypes.Mixed
                : resolveType(shape.ReturnType, true);

            if (shape.IsUnknownArity)
            {
                return new CallableCheckedType([], returnType, isAnyArity: true);
            }

            var parameters = shape.Parameters?.GetAllNotNull().ToList() ?? [];
            var parameterTypes = new List<ICheckedType>(parameters.Count);
            var flags = new List<(bool HasDefault, bool IsVariadic)>(parameters.Count);
            var names = new List<string?>(parameters.Count);
            foreach (var parameter in parameters)
            {
                parameterTypes.Add(
                    parameter.TypeExpression is null
                        ? CheckedTypes.Mixed
                        : resolveType(parameter.TypeExpression, false));
                flags.Add((parameter.IsOptional, parameter.IsVariadic));
                names.Add(CallableSignatureReflection.NormalizeParameterName(
                    parameter.SourceParameterName));
            }

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
        /// True when <paramref name="type"/> unwraps to the any-arity facet
        /// <c>callable(...): TReturn</c>.
        /// </summary>
        public static bool TryGetAnyArityFacet(ICheckedType type, out CallableCheckedType? facet)
        {
            facet = null;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is CallableCheckedType { IsAnyArity: true } direct)
            {
                facet = direct;
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when invoking <paramref name="type"/> would mean calling an any-arity facet
        /// (including <c>\Closure&lt;callable(...): R&gt;</c>). Type parameters constrained
        /// to that bound are not themselves the facet.
        /// </summary>
        public static bool IsAnyArityCallee(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TryGetAnyArityFacet(type, out _))
            {
                return true;
            }

            var facets = GetCallableFacets(type);
            return facets.Count > 0 && facets.All(candidate => candidate.IsAnyArity);
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
                if (candidate.IsAnyArity)
                {
                    continue;
                }

                if (candidate.LastParameterIsVariadic)
                {
                    var prefixCount = candidate.ParameterTypes.Count - 1;
                    if (argumentCount >= prefixCount)
                    {
                        facet = candidate;
                        return true;
                    }

                    continue;
                }

                if (candidate.ParameterTypes.Count == argumentCount)
                {
                    facet = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Selects a facet for an <c>$fn(...)</c> call that may include named arguments.
        /// When every named argument matches a stored parameter name, the facet that
        /// contains those names is preferred (so <c>$cb(b: true)</c> binds the optional
        /// second parameter rather than the shorter prefix). Unnamed shapes fall back to
        /// positional-plus-named arity and do not invent names.
        /// </summary>
        public static bool TrySelectCallableFacetForCall(
            ICheckedType type,
            int positionalCount,
            IReadOnlyList<string> namedArgumentNames,
            out CallableCheckedType? facet)
        {
            if (namedArgumentNames.Count == 0)
            {
                return TrySelectCallableFacet(type, positionalCount, out facet);
            }

            var facets = new List<CallableCheckedType>();
            foreach (var candidate in GetCallableFacets(type))
            {
                if (!candidate.IsAnyArity)
                {
                    facets.Add(candidate);
                }
            }

            if (facets.Count == 0)
            {
                facet = null;
                return false;
            }

            var matching = new List<CallableCheckedType>();
            foreach (var candidate in facets)
            {
                if (candidate.ParameterNames is null)
                {
                    continue;
                }

                var allFound = true;
                foreach (var name in namedArgumentNames)
                {
                    if (FindParameterIndexByName(candidate, name) < 0)
                    {
                        allFound = false;
                        break;
                    }
                }

                if (allFound)
                {
                    matching.Add(candidate);
                }
            }

            if (matching.Count > 0)
            {
                var wanted = positionalCount + namedArgumentNames.Count;
                CallableCheckedType? exact = null;
                CallableCheckedType? longest = null;
                foreach (var candidate in matching)
                {
                    if (longest is null
                        || candidate.ParameterTypes.Count > longest.ParameterTypes.Count)
                    {
                        longest = candidate;
                    }

                    if (candidate.LastParameterIsVariadic)
                    {
                        if (wanted >= candidate.ParameterTypes.Count - 1)
                        {
                            exact = candidate;
                        }
                    }
                    else if (candidate.ParameterTypes.Count == wanted)
                    {
                        exact = candidate;
                    }
                }

                facet = exact ?? longest;
                return facet is not null;
            }

            var anyNamed = false;
            foreach (var candidate in facets)
            {
                if (candidate.ParameterNames is not null)
                {
                    anyNamed = true;
                    break;
                }
            }

            if (!anyNamed)
            {
                return TrySelectCallableFacet(type, positionalCount + namedArgumentNames.Count, out facet);
            }

            CallableCheckedType? namedLongest = null;
            foreach (var candidate in facets)
            {
                if (candidate.ParameterNames is null)
                {
                    continue;
                }

                if (namedLongest is null
                    || candidate.ParameterTypes.Count > namedLongest.ParameterTypes.Count)
                {
                    namedLongest = candidate;
                }
            }

            if (namedLongest is not null)
            {
                facet = namedLongest;
                return true;
            }

            return TrySelectCallableFacet(type, positionalCount + namedArgumentNames.Count, out facet);
        }

        /// <summary>
        /// Case-insensitive lookup of a PHP named-argument key in
        /// <see cref="CallableCheckedType.ParameterNames"/>. Returns -1 when the facet
        /// has no names or the key is not present — never invents a name.
        /// </summary>
        public static int FindParameterIndexByName(CallableCheckedType facet, string? name)
        {
            if (string.IsNullOrEmpty(name) || facet.ParameterNames is null)
            {
                return -1;
            }

            var bare = name.TrimStart('$');
            for (var i = 0; i < facet.ParameterNames.Count; i++)
            {
                if (string.Equals(
                        facet.ParameterNames[i]?.TrimStart('$'),
                        bare,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Bare parameter names stored on <paramref name="facet"/> (no leading <c>$</c>),
        /// skipping unnamed slots.
        /// </summary>
        public static IReadOnlyList<string> CollectFacetParameterNames(CallableCheckedType facet)
        {
            if (facet.ParameterNames is null)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var name in facet.ParameterNames)
            {
                var bare = name?.TrimStart('$');
                if (!string.IsNullOrEmpty(bare))
                {
                    names.Add(bare);
                }
            }

            return names;
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
                if (candidate.IsAnyArity)
                {
                    continue;
                }

                if (candidate.ParameterTypes.Count == closureParameterCount)
                {
                    facet = candidate;
                    return true;
                }
            }

            CallableCheckedType? best = null;
            foreach (var candidate in facets)
            {
                if (candidate.IsAnyArity)
                {
                    continue;
                }

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
                GenericCheckedType { TypeArguments.Count: > 0 } generic
                    when IsClosureTypeName(generic.BaseType) =>
                    IsCallableFacetType(generic.TypeArguments[0]),
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

        /// <summary>
        /// Bare names of named (non-unpacked) arguments, in source order.
        /// </summary>
        public static IReadOnlyList<string> CollectNamedArgumentNames(PhpArgumentListAst? arguments)
        {
            if (arguments is null)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var argument in arguments.GetAllNotNull())
            {
                if (argument.IsVariadic || argument.Name is null)
                {
                    continue;
                }

                var bare = argument.Name.ValueString?.TrimStart('$');
                if (!string.IsNullOrEmpty(bare))
                {
                    names.Add(bare);
                }
            }

            return names;
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
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        CollectCallableFacets(member, results);
                    }

                    break;
                case IntersectionCheckedType intersection:
                    foreach (var member in intersection.Members)
                    {
                        CollectCallableFacets(member, results);
                    }

                    break;
                case GenericCheckedType { TypeArguments.Count: > 0 } generic
                    when IsClosureTypeName(generic.BaseType):
                    // Closure type args are class generics (`TCallableShape`, `TThis`, `TScope`).
                    // Arity facets come from TCallableShape / `__invoke`, not return-last.
                    CollectCallableFacets(generic.TypeArguments[0], results);
                    break;
                case SimpleCheckedType simple
                    when Rules.CheckerHelpers.IsBuiltInName(simple, "callable"):
                    results.Add(new CallableCheckedType([], CheckedTypes.Mixed));
                    break;
            }
        }

        /// <summary>
        /// True for the nominal <c>\Closure</c> class (bare or as a generic base). Name detection
        /// only — not return-last arity. Shared with intersection-member validation and the
        /// <c>Callable</c> constraint so both sides agree on what counts as the Closure class.
        /// </summary>
        public static bool IsClosureTypeName(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol.Name: "Closure" }
            || string.Equals(type.DisplayName, "Closure", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.DisplayName, "\\Closure", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True for bare <c>\Closure</c> or <c>\Closure&lt;…&gt;</c>. Invoke typing for those
        /// shapes uses <c>TCallableShape</c> (or stays gradual); do not fall back to the
        /// overlay <c>__invoke</c> member, which would collapse a typed Closure to mixed.
        /// </summary>
        public static bool IsClosureClassType(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (IsClosureTypeName(type))
            {
                return true;
            }

            return type is GenericCheckedType generic && IsClosureTypeName(generic.BaseType);
        }

        /// <summary>
        /// True when <paramref name="symbol"/> is the declaration of the root-namespace
        /// <c>\Closure</c> class (not a user type incidentally named <c>Closure</c> in another
        /// namespace). Used to let a fully bare <c>\Closure</c> reference stay open/gradual even
        /// though its <c>TCallableShape</c> parameter has no default.
        /// </summary>
        public static bool IsClosureDeclaration(IBaseSymbol? symbol)
        {
            if (symbol is not ObjectDeclarationSymbol obj)
            {
                return false;
            }

            var normalized = (obj.FullyQualifiedName ?? obj.Name ?? "").TrimStart('\\');
            if (string.Equals(normalized, "Closure", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.IsNullOrEmpty(obj.FullyQualifiedName)
                && string.Equals(obj.Name, "Closure", StringComparison.OrdinalIgnoreCase);
        }

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
