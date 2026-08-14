using System.Globalization;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Reflects an ordered parameter list and return type from a callable-ish
    /// <see cref="ICheckedType"/> (arity facets, <c>callable&lt;…&gt;</c> / <c>\Closure&lt;…&gt;</c>,
    /// or a binder parameter list from a function / method / closure symbol).
    /// </summary>
    internal static class CallableSignatureReflection
    {
        /// <summary>
        /// One reflected parameter. <see cref="Name"/> is absent for bare
        /// <c>callable&lt;…&gt;</c> facets that carry types but not parameter names.
        /// </summary>
        public sealed record Parameter(
            string? Name,
            ICheckedType Type,
            bool IsOptional,
            bool IsVariadic,
            bool IsByRef);

        /// <summary>
        /// Ordered parameters plus the callable's return type.
        /// </summary>
        public sealed class Signature
        {
            public Signature(IReadOnlyList<Parameter> parameters, ICheckedType returnType)
            {
                Parameters = parameters;
                ReturnType = returnType;
            }

            public IReadOnlyList<Parameter> Parameters { get; }

            public ICheckedType ReturnType { get; }
        }

        /// <summary>
        /// Reflects <paramref name="type"/> when it is (or unwraps to) one or more callable
        /// arity facets, a generic <c>callable</c>/<c>\Closure</c>, a bare opaque
        /// <c>callable</c>/<c>\Closure</c>, or a union of same-arity callables. Type parameters
        /// and non-callable types return false.
        /// </summary>
        /// <remarks>
        /// When several arity facets are present the reflected parameter list is the longest
        /// facet, and parameters beyond the shortest facet's arity are marked optional. That
        /// assumes the facets are arity siblings (shorter facets are prefixes of the longest),
        /// which is how <see cref="CallableArityFacetBuilder.Build"/> produces them from
        /// trailing defaults. The return type is taken from the first facet (arity siblings
        /// share a return; divergent hand-written intersections match
        /// <c>TryGetCallableReturnType</c>'s no-arity fallback). Facets from
        /// <c>callable&lt;…&gt;</c> type arguments do not encode names, by-ref, or variadic
        /// flags. Facets built from binder symbols via
        /// <see cref="CallableArityFacetBuilder.BuildFromParameterInfos"/> carry names so
        /// <c>__CallableParametersStruct</c> can key the named bag.
        /// <c>__CallableParametersTuple</c> uses positional types and does not need names
        /// (<c>0 as $_1</c>, <c>1 as $_2</c>, …). Unions of callables merge when every
        /// non-null member has the same non-variadic arity (parameter types are unioned; a
        /// slot is optional only when every member marks it optional). Opaque members and
        /// mismatched arities fail, so Rest unpack stays gradual. Use
        /// <see cref="FromParameterInfos"/> when a binder symbol is available directly.
        /// </remarks>
        public static bool TryReflect(ICheckedType type, out Signature? signature)
        {
            signature = null;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                return TryReflectUnion(union, out signature);
            }

            var facets = CallableArityFacetBuilder.GetCallableFacets(type);
            if (facets.Count == 0)
            {
                if (IsOpaqueCallable(type))
                {
                    signature = new Signature([], CheckedTypes.Mixed);
                    return true;
                }

                return false;
            }

            CallableCheckedType longest = facets[0];
            var shortestArity = facets[0].ParameterTypes.Count;
            foreach (var facet in facets)
            {
                if (facet.ParameterTypes.Count > longest.ParameterTypes.Count)
                {
                    longest = facet;
                }

                if (facet.ParameterTypes.Count < shortestArity)
                {
                    shortestArity = facet.ParameterTypes.Count;
                }
            }

            var parameters = new List<Parameter>(longest.ParameterTypes.Count);
            for (var i = 0; i < longest.ParameterTypes.Count; i++)
            {
                string? name = null;
                if (longest.ParameterNames is { } names && i < names.Count)
                {
                    name = names[i];
                }

                parameters.Add(new Parameter(
                    Name: name,
                    Type: longest.ParameterTypes[i],
                    IsOptional: i >= shortestArity,
                    IsVariadic: longest.LastParameterIsVariadic
                        && i == longest.ParameterTypes.Count - 1,
                    IsByRef: false));
            }

            // Parameter list comes from the longest facet (optional trailing params). Return type
            // uses the first facet when arity siblings share a return (the usual
            // <see cref="CallableArityFacetBuilder.Build"/> case) and the same first-facet
            // fallback as <c>TryGetCallableReturnType</c> when a hand-written intersection
            // diverges — type-position utilities have no selected call arity.
            signature = new Signature(parameters, facets[0].ReturnType);
            return true;
        }

        /// <summary>
        /// Unions of callables merge when every non-null member has the same non-variadic arity
        /// and the same variadic-ness. Parameter types are unioned; a slot is optional only when
        /// every member marks it optional (same policy as parameter-bag merge). Opaque members
        /// and mismatched arities fail — Rest unpack then stays gradual rather than inventing a
        /// 0-parameter list. Null union members are skipped.
        /// </summary>
        private static bool TryReflectUnion(UnionCheckedType union, out Signature? signature)
        {
            signature = null;
            Signature? merged = null;
            foreach (var member in union.Members)
            {
                if (TypeComparer.IsNullLiteral(member) || TypeComparer.IsBuiltInName(member, "null"))
                {
                    continue;
                }

                if (IsOpaqueCallable(member) || IsUnboundTypeParameter(member))
                {
                    return false;
                }

                if (!TryReflect(member, out var memberSignature) || memberSignature is null)
                {
                    return false;
                }

                if (merged is null)
                {
                    merged = memberSignature;
                    continue;
                }

                if (!TryMergeSignatures(merged, memberSignature, out merged) || merged is null)
                {
                    return false;
                }
            }

            if (merged is null)
            {
                return false;
            }

            signature = merged;
            return true;
        }

        private static bool TryMergeSignatures(Signature left, Signature right, out Signature? merged)
        {
            merged = null;
            var leftNonVariadic = new List<Parameter>();
            var rightNonVariadic = new List<Parameter>();
            Parameter? leftVariadic = null;
            Parameter? rightVariadic = null;
            foreach (var parameter in left.Parameters)
            {
                if (parameter.IsVariadic)
                {
                    leftVariadic = parameter;
                }
                else
                {
                    leftNonVariadic.Add(parameter);
                }
            }

            foreach (var parameter in right.Parameters)
            {
                if (parameter.IsVariadic)
                {
                    rightVariadic = parameter;
                }
                else
                {
                    rightNonVariadic.Add(parameter);
                }
            }

            if (leftNonVariadic.Count != rightNonVariadic.Count
                || (leftVariadic is null) != (rightVariadic is null))
            {
                return false;
            }

            var parameters = new List<Parameter>(left.Parameters.Count);
            for (var i = 0; i < leftNonVariadic.Count; i++)
            {
                var leftParam = leftNonVariadic[i];
                var rightParam = rightNonVariadic[i];
                var name = string.Equals(leftParam.Name, rightParam.Name, StringComparison.Ordinal)
                    ? leftParam.Name
                    : null;
                parameters.Add(new Parameter(
                    Name: name,
                    Type: CheckedTypes.UnionTypes(leftParam.Type, rightParam.Type),
                    IsOptional: leftParam.IsOptional && rightParam.IsOptional,
                    IsVariadic: false,
                    IsByRef: leftParam.IsByRef && rightParam.IsByRef));
            }

            if (leftVariadic is not null && rightVariadic is not null)
            {
                var name = string.Equals(leftVariadic.Name, rightVariadic.Name, StringComparison.Ordinal)
                    ? leftVariadic.Name
                    : null;
                parameters.Add(new Parameter(
                    Name: name,
                    Type: CheckedTypes.UnionTypes(leftVariadic.Type, rightVariadic.Type),
                    IsOptional: true,
                    IsVariadic: true,
                    IsByRef: leftVariadic.IsByRef && rightVariadic.IsByRef));
            }

            merged = new Signature(
                parameters,
                CheckedTypes.UnionTypes(left.ReturnType, right.ReturnType));
            return true;
        }

        /// <summary>
        /// Return type of a callable-ish <paramref name="type"/>. Unions of callables whose
        /// arities differ still produce a union of returns (Rest unpack cannot merge those
        /// parameter lists, but the return slot is covariant). Null members in a union are
        /// skipped (nullability of the callable is a call-site concern).
        /// </summary>
        public static bool TryGetReturnType(ICheckedType type, out ICheckedType returnType)
        {
            returnType = CheckedTypes.Unresolved;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                var returns = new List<ICheckedType>();
                foreach (var member in union.Members)
                {
                    if (TypeComparer.IsNullLiteral(member) || TypeComparer.IsBuiltInName(member, "null"))
                    {
                        continue;
                    }

                    if (!TryGetReturnType(member, out var memberReturn))
                    {
                        return false;
                    }

                    returns.Add(memberReturn);
                }

                if (returns.Count == 0)
                {
                    return false;
                }

                returnType = CheckedTypes.UnionTypes(returns);
                return true;
            }

            if (TryReflect(type, out var signature) && signature is not null)
            {
                returnType = signature.ReturnType;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds a named-parameter struct bag from a callable-ish <paramref name="type"/>.
        /// Non-variadic parameters with names become properties keyed <c>$name</c> (same convention
        /// as declared structs). Parameters with defaults (or beyond the shortest arity facet) are
        /// <see cref="StructPropertyInfo.IsOptional"/> so a partial bag can omit them without an
        /// exponential intersection of subset structs. Nameless facet parameters are skipped (the
        /// bag degrades). Unions of callables merge when every member has the same keys (property
        /// types are unioned; a key is optional only when every member marks it optional);
        /// mismatched keys degrade to an empty struct. Null union members are skipped. Variadic
        /// parameters are omitted — excess keys stay unknown-property errors, matching arity-facet
        /// policy (unbounded extra args are not modeled on the bag).
        /// </summary>
        public static bool TryBuildNamedParametersStruct(ICheckedType type, out StructCheckedType? bag) =>
            TryBuildParametersBag(type, BuildNamedStruct, out bag);

        /// <summary>
        /// Builds a positional-parameter struct bag from a callable-ish <paramref name="type"/>.
        /// Non-variadic parameters become <c>T i as $_(i+1)</c> (same shape as hand-written
        /// <c>CallableArgs*</c>). Names are not required — bare <c>callable&lt;…&gt;</c> facets
        /// still produce int keys. Defaulted / extra-arity parameters are optional fields so a
        /// shorter list literal can omit trailing keys. Unions merge when every member has the
        /// same keys; mismatched arities degrade to an empty struct. Null union members are
        /// skipped. Variadic parameters are omitted — extra indices stay unknown-property errors,
        /// matching arity-facet policy (unbounded extra args are not modeled on the bag).
        /// </summary>
        public static bool TryBuildPositionalParametersStruct(ICheckedType type, out StructCheckedType? bag) =>
            TryBuildParametersBag(type, BuildPositionalStruct, out bag);

        private static bool TryBuildParametersBag(
            ICheckedType type,
            Func<Signature, StructCheckedType> fromSignature,
            out StructCheckedType? bag)
        {
            bag = null;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                StructCheckedType? merged = null;
                foreach (var member in union.Members)
                {
                    if (TypeComparer.IsNullLiteral(member) || TypeComparer.IsBuiltInName(member, "null"))
                    {
                        continue;
                    }

                    if (!TryBuildParametersBag(member, fromSignature, out var memberBag) || memberBag is null)
                    {
                        return false;
                    }

                    if (merged is null)
                    {
                        merged = memberBag;
                        continue;
                    }

                    if (!TryMergeStructBags(merged, memberBag, out merged))
                    {
                        bag = new StructCheckedType(new Dictionary<string, StructPropertyInfo>());
                        return true;
                    }
                }

                if (merged is null)
                {
                    return false;
                }

                bag = merged;
                return true;
            }

            if (!TryReflect(type, out var signature) || signature is null)
            {
                return false;
            }

            bag = fromSignature(signature);
            return true;
        }

        private static StructCheckedType BuildNamedStruct(Signature signature)
        {
            var properties = new Dictionary<string, StructPropertyInfo>(StringComparer.Ordinal);
            foreach (var parameter in signature.Parameters)
            {
                if (parameter.IsVariadic || string.IsNullOrEmpty(parameter.Name))
                {
                    continue;
                }

                var key = parameter.Name.StartsWith('$') ? parameter.Name : "$" + parameter.Name;
                properties[key] = new StructPropertyInfo(
                    parameter.Type,
                    IsOptional: parameter.IsOptional);
            }

            return new StructCheckedType(properties);
        }

        /// <summary>
        /// CallableArgs-style property name for a zero-based positional index (<c>$_1</c> for 0).
        /// </summary>
        internal static string PositionalPropertyName(int zeroBasedIndex) =>
            "$_" + (zeroBasedIndex + 1).ToString(CultureInfo.InvariantCulture);

        private static StructCheckedType BuildPositionalStruct(Signature signature)
        {
            var properties = new Dictionary<string, StructPropertyInfo>(StringComparer.Ordinal);
            var index = 0;
            foreach (var parameter in signature.Parameters)
            {
                if (parameter.IsVariadic)
                {
                    continue;
                }

                properties[PositionalPropertyName(index)] = new StructPropertyInfo(
                    parameter.Type,
                    IntegerKeyAlias: index,
                    IsOptional: parameter.IsOptional);
                index++;
            }

            return new StructCheckedType(properties);
        }

        private static bool TryMergeStructBags(
            StructCheckedType left,
            StructCheckedType right,
            out StructCheckedType? merged)
        {
            merged = null;
            if (left.Properties.Count != right.Properties.Count
                || left.Properties.Keys.Any(key => !right.Properties.ContainsKey(key)))
            {
                return false;
            }

            var properties = new Dictionary<string, StructPropertyInfo>(StringComparer.Ordinal);
            foreach (var (key, leftProp) in left.Properties)
            {
                var rightProp = right.Properties[key];
                properties[key] = leftProp with
                {
                    Type = CheckedTypes.UnionTypes(leftProp.Type, rightProp.Type),
                    IsReadonly = leftProp.IsReadonly && rightProp.IsReadonly,
                    IsOptional = leftProp.IsOptional && rightProp.IsOptional,
                };
            }

            merged = new StructCheckedType(properties);
            return true;
        }

        /// <summary>
        /// True when <paramref name="type"/> unwraps to an in-scope generic type parameter
        /// (nullable wrappers included). Used to keep <c>__CallableReturnType&lt;TCallable&gt;</c>
        /// deferred until instantiation.
        /// </summary>
        public static bool IsUnboundTypeParameter(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol };
        }

        /// <summary>
        /// True when <paramref name="type"/> unwraps to a generic type parameter whose resolved
        /// constraint satisfies <c>Callable</c> (e.g. <c>TCallable extends callable</c>).
        /// </summary>
        public static bool TryUnwrapCallableTypeParameter(
            ICheckedType type,
            out ICheckedType parameterType)
        {
            parameterType = type;
            while (parameterType is NullableCheckedType nullable)
            {
                parameterType = nullable.InnerType;
            }

            if (parameterType is not SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint }
                })
            {
                return false;
            }

            return GenericTypeArgumentValidator.SatisfiesCallableConstraint(constraint);
        }

        /// <summary>
        /// Builds a signature from a binder parameter list whose types have already been
        /// resolved (function, method, or closure).
        /// </summary>
        public static Signature FromParameterInfos(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            ICheckedType returnType)
        {
            if (parameters.Count != parameterTypes.Count)
            {
                throw new ArgumentException(
                    "Parameter infos and types must have the same length.",
                    nameof(parameterTypes));
            }

            var reflected = new List<Parameter>(parameters.Count);
            for (var i = 0; i < parameters.Count; i++)
            {
                var info = parameters[i];
                reflected.Add(new Parameter(
                    Name: NormalizeParameterName(info.Name),
                    Type: parameterTypes[i],
                    IsOptional: info.DefaultValue is not null || info.IsVariadic,
                    IsVariadic: info.IsVariadic,
                    IsByRef: info.IsByReference));
            }

            return new Signature(reflected, returnType);
        }

        /// <summary>
        /// Builds a signature from closure / inline-function AST parameters whose types have
        /// already been resolved.
        /// </summary>
        public static Signature FromClosureParameters(
            IReadOnlyList<PhpParameterAst> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            ICheckedType returnType)
        {
            if (parameters.Count != parameterTypes.Count)
            {
                throw new ArgumentException(
                    "Parameter AST nodes and types must have the same length.",
                    nameof(parameterTypes));
            }

            var reflected = new List<Parameter>(parameters.Count);
            for (var i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                reflected.Add(new Parameter(
                    Name: NormalizeParameterName(param.Name),
                    Type: parameterTypes[i],
                    IsOptional: param.DefaultValue is not null || param.IsVariadic,
                    IsVariadic: param.IsVariadic,
                    IsByRef: param.IsRef));
            }

            return new Signature(reflected, returnType);
        }

        /// <summary>
        /// Strips a leading <c>$</c> from a binder/AST parameter name. Empty names become null
        /// so unnamed facet parameters stay distinct from a literally empty string key.
        /// </summary>
        public static string? NormalizeParameterName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var trimmed = name.TrimStart('$');
            return trimmed.Length == 0 ? null : trimmed;
        }

        /// <summary>
        /// Bare <c>callable</c> / <c>\Closure</c> written without a type-argument list. The
        /// <see cref="GenericCheckedType"/> exclusion is load-bearing: empty
        /// <c>callable&lt;&gt;</c> / <c>\Closure&lt;&gt;</c> still display as "callable" /
        /// "Closure", and those shapes are rejected by
        /// <c>GenericTypeArgumentValidator.SatisfiesCallableConstraint</c> rather than treated
        /// as opaque callables. Used by rest unpack to skip arity checks when the callable's
        /// parameter list is not statically known.
        /// </summary>
        internal static bool IsOpaqueCallable(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type is not GenericCheckedType
                && (Rules.CheckerHelpers.IsBuiltInName(type, "callable")
                    || CallableArityFacetBuilder.IsClosureTypeName(type));
        }

        /// <summary>
        /// Storage type of a variadic parameter inside the function body. Homogeneous
        /// <c>T ...$args</c> is <c>array&lt;int, T&gt;</c>. <c>__CallableParametersRest&lt;T&gt;</c>
        /// collects the unpacked arguments as the positional bag (or untyped <c>array</c> while
        /// <c>T</c> is still open).
        /// </summary>
        public static ICheckedType VariadicParameterStorageType(ICheckedType declaredElementType)
        {
            if (UtilityTypeResolver.TryGetCallableParametersRest(declaredElementType, out var callableArg))
            {
                return TryBuildPositionalParametersStruct(callableArg, out var bag) && bag is not null
                    ? bag
                    : CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array"));
            }

            return new GenericCheckedType(
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                [CheckedTypes.Int, declaredElementType]);
        }
    }
}
