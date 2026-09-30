using Tyhp.TyhpLang.Ast;
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
        /// Cycles return the alias symbol rather than recursing infinitely; the binder reports
        /// <c>BinderCircularTypeAlias</c> on cyclic declarations. The callback receives the alias
        /// being expanded so class-level bodies can resolve <c>self</c> against the owning class.
        /// </summary>
        public static ICheckedType ExpandTypeAliases(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, IBaseSymbol, ICheckedType> resolveAliasBody) =>
            ExpandTypeAliasesCore(type, symbolTree, globalScope, resolveAliasBody, new HashSet<IBaseSymbol>());

        private static ICheckedType ExpandTypeAliasesCore(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, IBaseSymbol, ICheckedType> resolveAliasBody,
            HashSet<IBaseSymbol> visiting)
        {
            switch (type)
            {
                case SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol alias }:
                    return ExpandAliasSymbol(
                        alias, alias.AliasedType, alias.GenericParameters, [],
                        symbolTree, globalScope, resolveAliasBody, visiting);

                case SimpleCheckedType { ResolvedSymbol: ObjectTypeAliasSymbol objectAlias }:
                    return ExpandAliasSymbol(
                        objectAlias, objectAlias.AliasedType, objectAlias.GenericParameters, [],
                        symbolTree, globalScope, resolveAliasBody, visiting);

                case GenericCheckedType generic when generic.BaseType is SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol alias }:
                    return ExpandAliasSymbol(
                        alias, alias.AliasedType, alias.GenericParameters, generic.TypeArguments,
                        symbolTree, globalScope, resolveAliasBody, visiting);

                case GenericCheckedType generic when generic.BaseType is SimpleCheckedType { ResolvedSymbol: ObjectTypeAliasSymbol objectAlias }:
                    return ExpandAliasSymbol(
                        objectAlias, objectAlias.AliasedType, objectAlias.GenericParameters, generic.TypeArguments,
                        symbolTree, globalScope, resolveAliasBody, visiting);

                case UnionCheckedType union:
                    return CheckedTypes.UnionTypes(
                        union.Members.Select(m => ExpandTypeAliasesCore(m, symbolTree, globalScope, resolveAliasBody, visiting)).ToList());

                case IntersectionCheckedType intersection:
                    return ExpandIntersectionAliases(
                        intersection, symbolTree, globalScope, resolveAliasBody, visiting);

                case NullableCheckedType nullable:
                {
                    var inner = ExpandTypeAliasesCore(nullable.InnerType, symbolTree, globalScope, resolveAliasBody, visiting);
                    return inner.IsNullable ? inner : new NullableCheckedType(inner);
                }

                case CallableCheckedType callable:
                    return callable.MapTypes(t =>
                        ExpandTypeAliasesCore(t, symbolTree, globalScope, resolveAliasBody, visiting));

                case HomogeneousVariadicCheckedType homogeneous:
                    return new HomogeneousVariadicCheckedType(
                        ExpandTypeAliasesCore(homogeneous.ElementType, symbolTree, globalScope, resolveAliasBody, visiting));

                case ParameterPackCheckedType pack:
                    return ParameterPack.MapPackMembers(
                        pack,
                        t => ExpandTypeAliasesCore(t, symbolTree, globalScope, resolveAliasBody, visiting));

                case GenericCheckedType generic:
                    return new GenericCheckedType(
                        ExpandTypeAliasesCore(generic.BaseType, symbolTree, globalScope, resolveAliasBody, visiting),
                        generic.TypeArguments
                            .Select(arg => ExpandTypeAliasesCore(arg, symbolTree, globalScope, resolveAliasBody, visiting))
                            .ToList())
                    {
                        IsNonRebindableClosure = generic.IsNonRebindableClosure,
                    };

                default:
                    return type;
            }
        }

        /// <summary>
        /// Expands aliases inside a declared intersection without using the narrowing
        /// <see cref="IntersectTypes"/> meet. That meet collapses unrelated class-likes to
        /// <c>never</c> (neither arm is assignable to the other), which is wrong for an
        /// annotation such as <c>A&amp;B</c> of two interfaces. Non-inhabitable members
        /// (<c>int&amp;string</c>, <c>callable&amp;array</c>) still fold through the meet.
        /// </summary>
        private static ICheckedType ExpandIntersectionAliases(
            IntersectionCheckedType intersection,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, IBaseSymbol, ICheckedType> resolveAliasBody,
            HashSet<IBaseSymbol> visiting)
        {
            var expandedMembers = new List<ICheckedType>();
            foreach (var member in intersection.Members)
            {
                var expanded = ExpandTypeAliasesCore(
                    member, symbolTree, globalScope, resolveAliasBody, visiting);
                if (IsNeverType(expanded))
                {
                    return CheckedTypes.Never;
                }

                if (IsMixedType(expanded) || IsUnresolvedType(expanded))
                {
                    continue;
                }

                AppendIntersectionMembers(expandedMembers, expanded);
            }

            if (expandedMembers.Count == 0)
            {
                return CheckedTypes.Never;
            }

            if (expandedMembers.All(IsDeclaredIntersectionMember))
            {
                return BuildDeclaredIntersection(expandedMembers, symbolTree, globalScope);
            }

            ICheckedType? result = null;
            foreach (var expanded in expandedMembers)
            {
                result = result is null
                    ? expanded
                    : IntersectTypes(result, expanded, symbolTree, globalScope);
            }

            return result ?? CheckedTypes.Unresolved;
        }

        private static void AppendIntersectionMembers(List<ICheckedType> dest, ICheckedType type)
        {
            if (type is IntersectionCheckedType nested)
            {
                foreach (var member in nested.Members)
                {
                    AppendIntersectionMembers(dest, member);
                }

                return;
            }

            dest.Add(type);
        }

        /// <summary>
        /// Types that can appear together in a declared intersection without proving the
        /// combination uninhabitable. Scalars, <c>array</c>, and similar stay on the
        /// narrowing-meet path so <c>int&amp;string</c> / <c>callable&amp;array</c> remain
        /// <c>never</c>.
        /// </summary>
        private static bool IsDeclaredIntersectionMember(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is IntersectionCheckedType nested)
            {
                return nested.Members.Count > 0 && nested.Members.All(IsDeclaredIntersectionMember);
            }

            if (type is StructCheckedType || type is CallableCheckedType || type is ObjectShapeCheckedType)
            {
                return true;
            }

            if (type is GenericCheckedType generic)
            {
                return IsDeclaredIntersectionMember(generic.BaseType)
                    || CallableArityFacetBuilder.IsCallableFacetType(type);
            }

            if (type is StaticCheckedType staticType)
            {
                return IsDeclaredIntersectionMember(staticType.DeclaringType);
            }

            if (IsBuiltInName(type, "object")
                || IsBuiltInName(type, "struct")
                || IsBuiltInName(type, "self")
                || IsBuiltInName(type, "static")
                || IsBuiltInName(type, "parent"))
            {
                return true;
            }

            if (CallableArityFacetBuilder.IsCallableFacetType(type)
                || CallableArityFacetBuilder.IsClosureTypeName(type))
            {
                return true;
            }

            var symbol = TryGetNominalSymbol(type);
            return symbol is ObjectDeclarationSymbol or GenericTypeParameterSymbol;
        }

        private static ICheckedType BuildDeclaredIntersection(
            List<ICheckedType> members,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var deduped = new List<ICheckedType>();
            foreach (var member in members)
            {
                if (!deduped.Any(existing => AreTypesEqual(existing, member)))
                {
                    deduped.Add(member);
                }
            }

            var simplified = new List<ICheckedType>();
            foreach (var member in deduped)
            {
                var redundantSupertype = deduped.Any(other =>
                    !ReferenceEquals(other, member)
                    && !AreTypesEqual(other, member)
                    && IsSubtypeOf(other, member, symbolTree, globalScope)
                    && !IsSubtypeOf(member, other, symbolTree, globalScope));
                if (!redundantSupertype)
                {
                    simplified.Add(member);
                }
            }

            return simplified.Count switch
            {
                0 => CheckedTypes.Never,
                1 => simplified[0],
                _ => new IntersectionCheckedType(simplified),
            };
        }

        private static ICheckedType ExpandAliasSymbol(
            IBaseSymbol alias,
            ITypeExpression? aliasedType,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IReadOnlyList<ICheckedType> typeArguments,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, IBaseSymbol, ICheckedType> resolveAliasBody,
            HashSet<IBaseSymbol> visiting)
        {
            if (!visiting.Add(alias))
            {
                if (ObjectShapeSupport.TryGetObjectShape(alias) is { } cycleShape)
                {
                    return new ObjectShapeCheckedType(cycleShape, alias, typeArguments);
                }

                return CheckedTypes.FromSymbol(alias);
            }

            if (aliasedType is null)
            {
                visiting.Remove(alias);
                return CheckedTypes.FromSymbol(alias);
            }

            if (aliasedType is TyhpObjectShapeAst shape)
            {
                // Member signatures (parameter/return/property/const types) are resolved by
                // `resolveAliasBody` alone, which — like `ExpandTypeAliases`'s own base case —
                // only produces a *bare* alias-wrapped SimpleCheckedType, not an expanded shape.
                // Without also running that result through ExpandTypeAliasesCore here, a member
                // that names another object-shape alias (`public function address(): Address;`,
                // or a mutually-recursive `A`/`B` pair) would stay unexpanded, and assignability
                // against it would see `TryAsObjectShape`'s Members-less identity-only wrapper
                // instead of the alias's real member map (FOUND: nested shape member — see
                // ObjectShapeAssignabilityTests). The same `visiting` set already guards this
                // alias against infinite recursion, so self- and mutually-recursive shapes still
                // bottom out at the existing cycle-stub branch above.
                var members = ObjectShapeMemberBuilder.Build(
                    shape,
                    (typeAst, _) => ExpandTypeAliasesCore(
                        resolveAliasBody(typeAst, alias), symbolTree, globalScope, resolveAliasBody, visiting));
                ICheckedType result = new ObjectShapeCheckedType(shape, alias, typeArguments, members);
                if (typeArguments.Count > 0 && genericParameters.Count > 0)
                {
                    var substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    for (var i = 0; i < Math.Min(genericParameters.Count, typeArguments.Count); i++)
                    {
                        substitutions[genericParameters[i].Name] = typeArguments[i];
                    }

                    result = ResolveGenericType(result, substitutions, symbolTree, globalScope);
                }

                visiting.Remove(alias);
                return result;
            }

            var resolved = resolveAliasBody(aliasedType, alias);

            if (typeArguments.Count > 0 && genericParameters.Count > 0)
            {
                var substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                for (var i = 0; i < Math.Min(genericParameters.Count, typeArguments.Count); i++)
                {
                    substitutions[genericParameters[i].Name] = typeArguments[i];
                }

                resolved = ResolveGenericType(resolved, substitutions, symbolTree, globalScope);
            }

            var expanded = ExpandTypeAliasesCore(resolved, symbolTree, globalScope, resolveAliasBody, visiting);
            visiting.Remove(alias);
            return expanded;
        }
    }
}
