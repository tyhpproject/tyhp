using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Pack detection, splice of Rest/pack parameters on a callable shape, pack-preserving
    /// <c>__Nullable</c> / <c>__NonNullable</c>, and <c>__CallableParametersSlice</c> metadata.
    /// </summary>
    internal static class ParameterPack
    {
        public readonly record struct SliceInfo(
            ICheckedType CallableArg,
            int Start,
            int Min,
            bool MinSpecified);

        public static bool IsPack(ICheckedType type)
        {
            if (type is ParameterPackCheckedType)
            {
                return true;
            }

            return UtilityTypeResolver.TryGetCallableParametersRest(type, out _);
        }

        public static bool TryExpandPack(
            ICheckedType type,
            out IReadOnlyList<ICheckedType> members,
            out bool lastMemberIsVariadic)
        {
            members = [];
            lastMemberIsVariadic = false;

            if (type is ParameterPackCheckedType pack)
            {
                if (pack.Members.Count > 0)
                {
                    members = pack.Members;
                    lastMemberIsVariadic = pack.LastMemberIsVariadic;
                    return true;
                }

                if (pack.SourceCallable is not null
                    && TryReflectParameterTypes(pack.SourceCallable, out var reflected, out lastMemberIsVariadic))
                {
                    members = MapMembers(reflected, pack.Map);
                    return true;
                }

                return false;
            }

            if (UtilityTypeResolver.TryGetCallableParametersRest(type, out var callable)
                && TryReflectParameterTypes(callable, out var restMembers, out lastMemberIsVariadic))
            {
                members = restMembers;
                return true;
            }

            return false;
        }

        public static IReadOnlyList<ICheckedType> SpliceParameterList(
            IReadOnlyList<ICheckedType> typeArguments,
            out bool lastParameterIsVariadic)
        {
            lastParameterIsVariadic = false;
            var spliced = new List<ICheckedType>(typeArguments.Count);
            foreach (var arg in typeArguments)
            {
                if (arg is HomogeneousVariadicCheckedType homogeneous)
                {
                    spliced.Add(homogeneous.ElementType);
                    lastParameterIsVariadic = true;
                    continue;
                }

                if (TryExpandPack(arg, out var members, out var packVariadic))
                {
                    spliced.AddRange(members);
                    lastParameterIsVariadic = packVariadic;
                    continue;
                }

                if (IsPack(arg))
                {
                    spliced.Add(arg);
                    continue;
                }

                spliced.Add(arg);
            }

            return spliced;
        }

        public static ICheckedType ApplyNullable(ICheckedType inner)
        {
            if (TryExpandPack(inner, out var members, out var lastVariadic))
            {
                return new ParameterPackCheckedType(
                    MapMembers(members, ParameterPackMap.Nullable),
                    lastMemberIsVariadic: lastVariadic);
            }

            if (UtilityTypeResolver.TryGetCallableParametersRest(inner, out var callable))
            {
                return new ParameterPackCheckedType(
                    [],
                    callable,
                    ParameterPackMap.Nullable);
            }

            if (inner is ParameterPackCheckedType pack)
            {
                return ComposeMap(pack, ParameterPackMap.Nullable);
            }

            return inner.IsNullable ? inner : new NullableCheckedType(inner);
        }

        public static ICheckedType ApplyNonNullable(ICheckedType inner)
        {
            if (TryExpandPack(inner, out var members, out var lastVariadic))
            {
                return new ParameterPackCheckedType(
                    MapMembers(members, ParameterPackMap.NonNullable),
                    lastMemberIsVariadic: lastVariadic);
            }

            if (UtilityTypeResolver.TryGetCallableParametersRest(inner, out var callable))
            {
                return new ParameterPackCheckedType(
                    [],
                    callable,
                    ParameterPackMap.NonNullable);
            }

            if (inner is ParameterPackCheckedType pack)
            {
                return ComposeMap(pack, ParameterPackMap.NonNullable);
            }

            return null;
        }

        public static bool TryGetSlice(ICheckedType type, out SliceInfo info)
        {
            info = default;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is not GenericCheckedType { TypeArguments.Count: > 0 } generic
                || !SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility)
                || utility.Behavior != UtilityBehavior.CallableParametersSlice)
            {
                return false;
            }

            var args = generic.TypeArguments;
            var start = 0;
            var min = 0;
            if (args.Count >= 2)
            {
                if (!TryReadNonNegativeInt(args[1], out start))
                {
                    return false;
                }
            }

            var minSpecified = args.Count >= 3;
            if (minSpecified && !TryReadNonNegativeInt(args[2], out min))
            {
                return false;
            }

            info = new SliceInfo(args[0], start, min, minSpecified);
            return true;
        }

        /// <summary>
        /// Unwraps <c>array&lt;Slice&lt;…&gt;&gt;</c> (and the one-arg array shorthand) to the
        /// inner Slice wrapper.
        /// </summary>
        public static bool TryGetArraySlice(ICheckedType type, out SliceInfo info)
        {
            info = default;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is GenericCheckedType generic
                && generic.TypeArguments.Count > 0
                && IsArrayBase(generic.BaseType))
            {
                return TryGetSlice(generic.TypeArguments[^1], out info);
            }

            return false;
        }

        public static bool TryGetSliceParameterAt(
            SliceInfo info,
            int offset,
            out ICheckedType parameterType)
        {
            parameterType = CheckedTypes.Unresolved;
            var index = info.Start + offset;
            if (!TryReflectParameterTypes(info.CallableArg, out var members, out var lastVariadic))
            {
                return false;
            }

            if (index < members.Count)
            {
                parameterType = members[index];
                return true;
            }

            if (lastVariadic && members.Count > 0)
            {
                parameterType = members[^1];
                return true;
            }

            return false;
        }

        public static ICheckedType GetArrayElementType(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is GenericCheckedType generic
                && generic.TypeArguments.Count > 0
                && IsArrayBase(generic.BaseType))
            {
                return generic.TypeArguments[^1];
            }

            return type;
        }

        public static bool TryReadNonNegativeInt(ICheckedType type, out int value)
        {
            value = 0;
            if (!TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(type, out var literal)
                || literal < 0
                || literal > int.MaxValue)
            {
                return false;
            }

            value = (int)literal;
            return true;
        }

        public static bool TypeParamHasRestOrSliceFeed(
            GenericTypeParameterSymbol param,
            IReadOnlyList<ParameterInfo>? calleeParameters)
        {
            if (calleeParameters is null || calleeParameters.Count == 0)
            {
                return false;
            }

            foreach (var parameter in calleeParameters)
            {
                if (DeclaredTypeFeedsCallableUtility(parameter.DeclaredType, param))
                {
                    return true;
                }
            }

            return false;
        }

        public static ICheckedType MapPackMembers(ParameterPackCheckedType pack, Func<ICheckedType, ICheckedType> map)
        {
            if (pack.Members.Count > 0)
            {
                var mapped = pack.Members.Select(map).ToList();
                return new ParameterPackCheckedType(
                    mapped,
                    pack.SourceCallable is null ? null : map(pack.SourceCallable),
                    pack.Map,
                    pack.LastMemberIsVariadic);
            }

            return new ParameterPackCheckedType(
                [],
                pack.SourceCallable is null ? null : map(pack.SourceCallable),
                pack.Map);
        }

        private static bool DeclaredTypeFeedsCallableUtility(
            ITypeExpression? typeAst,
            GenericTypeParameterSymbol param)
        {
            var inner = UnwrapArrayTypeAst(UnwrapSingleMemberTypeExpression(typeAst));
            if (inner is not PhpNamedTypeAst named)
            {
                return false;
            }

            var name = GetNamedTypeIdentifier(named);
            if (!name.Contains("CallableParametersRest", StringComparison.Ordinal)
                && !name.Contains("CallableParametersSlice", StringComparison.Ordinal))
            {
                return false;
            }

            if (!named.AstGrammarAddons.TryGetValue("typeName", out var addon)
                || addon is not PhpTypeExpressionListAst list)
            {
                return false;
            }

            var first = UnwrapSingleMemberTypeExpression(list.GetAllNotNull().FirstOrDefault());
            return TypeAstNamesParameter(first, param);
        }

        /// <summary>
        /// Parameter/return type annotations wrap their real type in a <see
        /// cref="PhpTypeExpressionAst"/> "type expression" container (its <c>Types</c> list holds
        /// one member for a non-union annotation, several for <c>int|string</c>). AST-level checks
        /// like <see cref="DeclaredTypeFeedsCallableUtility"/> that inspect the raw declared-type
        /// AST (rather than the resolved <see cref="ICheckedType"/>) must unwrap this container —
        /// otherwise a single-type annotation like <c>__CallableParametersSlice&lt;TCallable,
        /// 0&gt;</c> never matches <see cref="PhpNamedTypeAst"/> below.
        /// </summary>
        private static ITypeExpression? UnwrapSingleMemberTypeExpression(ITypeExpression? typeAst)
        {
            while (typeAst is PhpTypeExpressionAst wrapper)
            {
                var members = wrapper.Types?.GetAllNotNull().ToList();
                if (members is not { Count: 1 })
                {
                    return typeAst;
                }

                typeAst = members[0];
            }

            return typeAst;
        }

        private static ITypeExpression? UnwrapArrayTypeAst(ITypeExpression? typeAst)
        {
            if (typeAst is PhpNamedTypeAst named
                && string.Equals(GetNamedTypeIdentifier(named), "array", StringComparison.OrdinalIgnoreCase)
                && named.AstGrammarAddons.TryGetValue("typeName", out var addon)
                && addon is PhpTypeExpressionListAst list)
            {
                var args = list.GetAllNotNull().ToList();
                if (args.Count > 0)
                {
                    return UnwrapSingleMemberTypeExpression(args[^1]);
                }
            }

            return typeAst;
        }

        /// <summary>
        /// <see cref="PhpNamedTypeAst"/> does not carry its own <c>Identifier</c> (that base
        /// property is only populated on nodes the visitor sets it on directly) — the name lives
        /// on the wrapped <see cref="PhpNamedTypeAst.Name"/> expression. Same lookup order as
        /// <c>GenericTypeArgumentValidator</c> / <c>DeclarationRule.ObjectType</c>.
        /// </summary>
        private static string GetNamedTypeIdentifier(PhpNamedTypeAst named) =>
            named.Name?.ValueString ?? named.Name?.Identifier ?? named.Identifier ?? string.Empty;

        private static bool TypeAstNamesParameter(ITypeExpression? typeAst, GenericTypeParameterSymbol param)
        {
            return typeAst switch
            {
                PhpNamedTypeAst named =>
                    string.Equals(GetNamedTypeIdentifier(named), param.Name, StringComparison.Ordinal),
                PhpBuiltinTypeAst builtin =>
                    string.Equals(builtin.Identifier, param.Name, StringComparison.Ordinal),
                _ => false,
            };
        }

        private static ParameterPackCheckedType ComposeMap(ParameterPackCheckedType pack, ParameterPackMap extra)
        {
            var composed = extra == ParameterPackMap.Identity
                ? pack.Map
                : extra;
            if (pack.Members.Count > 0)
            {
                return new ParameterPackCheckedType(
                    MapMembers(pack.Members, extra),
                    pack.SourceCallable,
                    composed,
                    pack.LastMemberIsVariadic);
            }

            return new ParameterPackCheckedType([], pack.SourceCallable, composed);
        }

        private static IReadOnlyList<ICheckedType> MapMembers(
            IReadOnlyList<ICheckedType> members,
            ParameterPackMap map)
        {
            if (map == ParameterPackMap.Identity)
            {
                return members;
            }

            var mapped = new List<ICheckedType>(members.Count);
            foreach (var member in members)
            {
                mapped.Add(map switch
                {
                    ParameterPackMap.Nullable =>
                        member.IsNullable ? member : new NullableCheckedType(member),
                    ParameterPackMap.NonNullable => StripNull(member),
                    _ => member,
                });
            }

            return mapped;
        }

        private static ICheckedType StripNull(ICheckedType type)
        {
            if (type is LiteralCheckedType { Value: null }
                || CheckedTypes.AreTypesEqual(type, CheckedTypes.Null))
            {
                return CheckedTypes.Void;
            }

            if (type is NullableCheckedType nullable)
            {
                return StripNull(nullable.InnerType);
            }

            if (type is UnionCheckedType union)
            {
                var remaining = union.Members
                    .Where(member =>
                        member is not LiteralCheckedType { Value: null }
                        && !CheckedTypes.AreTypesEqual(member, CheckedTypes.Null))
                    .Select(StripNull)
                    .ToList();
                return remaining.Count == 0 ? CheckedTypes.Void : CheckedTypes.UnionTypes(remaining);
            }

            return type;
        }

        private static bool TryReflectParameterTypes(
            ICheckedType callable,
            out IReadOnlyList<ICheckedType> members,
            out bool lastMemberIsVariadic)
        {
            members = [];
            lastMemberIsVariadic = false;
            if (!CallableSignatureReflection.TryReflect(callable, out var signature)
                || signature is null)
            {
                return false;
            }

            var types = new List<ICheckedType>(signature.Parameters.Count);
            foreach (var parameter in signature.Parameters)
            {
                types.Add(parameter.Type);
                lastMemberIsVariadic = parameter.IsVariadic;
            }

            members = types;
            return true;
        }

        private static bool IsArrayBase(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol.Name: "array" }
            || string.Equals(type.DisplayName, "array", StringComparison.OrdinalIgnoreCase);
    }
}
