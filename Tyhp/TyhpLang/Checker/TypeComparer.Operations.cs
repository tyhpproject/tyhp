using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        private static bool AreTypesEqualCore(
            ICheckedType? left,
            ICheckedType? right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            if (!visited.Add((left, right)))
            {
                return true;
            }

            if (left.Kind != right.Kind)
            {
                return false;
            }

            return left.Kind switch
            {
                CheckedTypeKind.Simple =>
                    left is SimpleCheckedType ls &&
                    right is SimpleCheckedType rs &&
                    SymbolsMatch(ls.ResolvedSymbol, rs.ResolvedSymbol),
                CheckedTypeKind.Union =>
                    left is UnionCheckedType lu &&
                    right is UnionCheckedType ru &&
                    HaveSameTypeSet(lu.Members, ru.Members, visited),
                CheckedTypeKind.Intersection =>
                    left is IntersectionCheckedType li &&
                    right is IntersectionCheckedType ri &&
                    HaveSameTypeSet(li.Members, ri.Members, visited),
                CheckedTypeKind.Nullable =>
                    left is NullableCheckedType ln &&
                    right is NullableCheckedType rn &&
                    AreTypesEqualCore(ln.InnerType, rn.InnerType, visited),
                CheckedTypeKind.Generic =>
                    left is GenericCheckedType lg &&
                    right is GenericCheckedType rg &&
                    lg.TypeArguments.Count == rg.TypeArguments.Count &&
                    lg.TypeArguments.Zip(rg.TypeArguments).All(pair =>
                        AreTypesEqualCore(pair.First, pair.Second, visited)) &&
                    (AreTypesEqualCore(lg.BaseType, rg.BaseType, visited)
                        || AreEquivalentCallableReturnUtilities(lg.BaseType, rg.BaseType)),
                CheckedTypeKind.Literal =>
                    left is LiteralCheckedType ll &&
                    right is LiteralCheckedType rl &&
                    Equals(ll.Value, rl.Value) &&
                    AreTypesEqualCore(ll.UnderlyingType, rl.UnderlyingType, visited),
                CheckedTypeKind.Struct =>
                    left is StructCheckedType ls &&
                    right is StructCheckedType rs &&
                    ls.Properties.Count == rs.Properties.Count &&
                    ls.Properties.All(pair =>
                        rs.Properties.TryGetValue(pair.Key, out var other) &&
                        pair.Value.IsReadonly == other.IsReadonly &&
                        pair.Value.IntegerKeyAlias == other.IntegerKeyAlias &&
                        pair.Value.IsOptional == other.IsOptional &&
                        AreTypesEqualCore(pair.Value.Type, other.Type, visited)),
                CheckedTypeKind.Callable =>
                    left is CallableCheckedType lc &&
                    right is CallableCheckedType rc &&
                    lc.ParameterTypes.Count == rc.ParameterTypes.Count &&
                    lc.ParameterTypes.Zip(rc.ParameterTypes).All(pair =>
                        AreTypesEqualCore(pair.First, pair.Second, visited)) &&
                    AreTypesEqualCore(lc.ReturnType, rc.ReturnType, visited),
                CheckedTypeKind.Static =>
                    left is StaticCheckedType lStatic &&
                    right is StaticCheckedType rStatic &&
                    AreTypesEqualCore(lStatic.DeclaringType, rStatic.DeclaringType, visited),
                CheckedTypeKind.Never or CheckedTypeKind.Void or CheckedTypeKind.Mixed or CheckedTypeKind.Unresolved =>
                    left.DisplayName == right.DisplayName,
                _ => left.DisplayName == right.DisplayName,
            };
        }

        private static bool HaveSameTypeSet(
            IReadOnlyList<ICheckedType> left,
            IReadOnlyList<ICheckedType> right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            return left.All(leftMember =>
                right.Any(rightMember => AreTypesEqualCore(leftMember, rightMember, visited)));
        }

        /// <summary>
        /// <c>\Tyhp\ReturnType</c> and <c>__CallableReturnType</c> are the same checker operation
        /// (callable-keyed return extraction). Deferred wrappers must compare equal so
        /// <c>return $cb()</c> type-checks when the declared return uses either spelling.
        /// </summary>
        private static bool AreEquivalentCallableReturnUtilities(ICheckedType leftBase, ICheckedType rightBase) =>
            SymbolNameTypeHelper.TryGetUtilitySymbol(leftBase, out var left)
            && SymbolNameTypeHelper.TryGetUtilitySymbol(rightBase, out var right)
            && IsCallableReturnUtility(left.Behavior)
            && IsCallableReturnUtility(right.Behavior);

        private static bool IsCallableReturnUtility(UtilityBehavior behavior) =>
            behavior is UtilityBehavior.ReturnType or UtilityBehavior.CallableReturnType;

        private static ICheckedType UnionTypesCore(
            IReadOnlyList<ICheckedType> members,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var flattened = new List<ICheckedType>();
            foreach (var member in members)
            {
                if (member is UnionCheckedType union)
                {
                    flattened.AddRange(union.Members);
                }
                else
                {
                    flattened.Add(member);
                }
            }

            var working = new List<ICheckedType>();
            foreach (var member in flattened)
            {
                if (IsUnresolvedType(member))
                {
                    continue;
                }

                if (IsNeverType(member))
                {
                    continue;
                }

                if (IsMixedType(member))
                {
                    return CheckedTypes.Mixed;
                }

                working.Add(member);
            }

            if (working.Count == 0)
            {
                return CheckedTypes.Never;
            }

            working = SimplifyBoolLiterals(working);

            var deduped = new List<ICheckedType>();
            foreach (var member in working)
            {
                if (!deduped.Any(existing => AreTypesEqual(existing, member)))
                {
                    deduped.Add(member);
                }
            }

            var subsumedRemoved = RemoveSubsumedMembers(deduped, symbolTree, globalScope);

            return subsumedRemoved.Count switch
            {
                0 => CheckedTypes.Never,
                1 => subsumedRemoved[0],
                _ => new UnionCheckedType(subsumedRemoved),
            };
        }

        private static ICheckedType IntersectTypesCore(
            ICheckedType a,
            ICheckedType b,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (IsUnresolvedType(a))
            {
                return b;
            }

            if (IsUnresolvedType(b))
            {
                return a;
            }

            if (IsMixedType(a))
            {
                return b;
            }

            if (IsMixedType(b))
            {
                return a;
            }

            if (IsNeverType(a) || IsNeverType(b))
            {
                return CheckedTypes.Never;
            }

            if (AreTypesEqual(a, b))
            {
                return a;
            }

            if (IsSubtypeOf(a, b, symbolTree, globalScope))
            {
                return a;
            }

            if (IsSubtypeOf(b, a, symbolTree, globalScope))
            {
                return b;
            }

            if (a is NullableCheckedType nullableA)
            {
                var inner = IntersectTypesCore(nullableA.InnerType, b, symbolTree, globalScope);
                return inner.IsNever ? CheckedTypes.Never : new NullableCheckedType(inner);
            }

            if (b is NullableCheckedType nullableB)
            {
                var inner = IntersectTypesCore(a, nullableB.InnerType, symbolTree, globalScope);
                return inner.IsNever ? CheckedTypes.Never : new NullableCheckedType(inner);
            }

            if (a is IntersectionCheckedType intersectionA)
            {
                var result = b;
                foreach (var member in intersectionA.Members)
                {
                    result = IntersectTypesCore(member, result, symbolTree, globalScope);
                }

                return result;
            }

            if (b is IntersectionCheckedType intersectionB)
            {
                var result = a;
                foreach (var member in intersectionB.Members)
                {
                    result = IntersectTypesCore(result, member, symbolTree, globalScope);
                }

                return result;
            }

            // Callable arity facets are siblings (optional trailing defaults), not a subtype chain.
            // Their meet must remain an intersection — the same model Story 27 uses for `new<>`.
            if (a is CallableCheckedType && b is CallableCheckedType)
            {
                return new IntersectionCheckedType([a, b]);
            }

            if (!IsAssignableTo(a, b, symbolTree, globalScope) && !IsAssignableTo(b, a, symbolTree, globalScope))
            {
                return CheckedTypes.Never;
            }

            return new IntersectionCheckedType([a, b]);
        }

        private static ICheckedType NarrowTypeCore(
            ICheckedType current,
            ICheckedType narrowTo,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (IsUnresolvedType(current) || IsUnresolvedType(narrowTo))
            {
                return narrowTo;
            }

            if (IsNullLiteral(narrowTo))
            {
                return CheckedTypes.Null;
            }

            if (current is NullableCheckedType nullableCurrent && !narrowTo.IsNullable && !IsNullLiteral(narrowTo))
            {
                var withoutNull = nullableCurrent.InnerType;
                if (IsAssignableTo(narrowTo, withoutNull, symbolTree, globalScope))
                {
                    return narrowTo;
                }
            }

            if (current is UnionCheckedType union)
            {
                var matching = union.Members
                    .Where(member => IsAssignableTo(member, narrowTo, symbolTree, globalScope) ||
                                     IsSubtypeOf(member, narrowTo, symbolTree, globalScope) ||
                                     AreTypesEqual(member, narrowTo))
                    .ToList();

                if (matching.Count == 1)
                {
                    return matching[0];
                }

                if (matching.Count > 1)
                {
                    return UnionTypes(matching, symbolTree, globalScope);
                }
            }

            if (IsSubtypeOf(narrowTo, current, symbolTree, globalScope))
            {
                return narrowTo;
            }

            return IntersectTypes(current, narrowTo, symbolTree, globalScope);
        }

        private static ICheckedType NarrowTypeNegativeCore(
            ICheckedType current,
            ICheckedType excludeType,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (IsUnresolvedType(current))
            {
                return current;
            }

            if (current is UnionCheckedType union)
            {
                var remaining = union.Members
                    .Where(member => !AreTypesEqual(member, excludeType) &&
                                     !IsSubtypeOf(member, excludeType, symbolTree, globalScope))
                    .ToList();

                return remaining.Count switch
                {
                    0 => CheckedTypes.Never,
                    1 => remaining[0],
                    _ => UnionTypes(remaining, symbolTree, globalScope),
                };
            }

            if (AreTypesEqual(current, excludeType) || IsSubtypeOf(current, excludeType, symbolTree, globalScope))
            {
                return CheckedTypes.Never;
            }

            return current;
        }

        /// <summary>
        /// Collapses a coexisting <c>true</c> and <c>false</c> literal pair into a single <c>bool</c>
        /// (§3.10). Other union members are preserved; if both literals are not present the list is
        /// returned unchanged.
        /// </summary>
        private static List<ICheckedType> SimplifyBoolLiterals(List<ICheckedType> members)
        {
            var hasTrue = members.Any(m => m is LiteralCheckedType { Value: true } || IsBuiltInName(m, "true"));
            var hasFalse = members.Any(m => m is LiteralCheckedType { Value: false } || IsBuiltInName(m, "false"));

            if (!hasTrue || !hasFalse)
            {
                return members;
            }

            var result = new List<ICheckedType>();
            var boolAdded = false;
            foreach (var member in members)
            {
                var isBoolLiteral =
                    member is LiteralCheckedType { Value: bool } ||
                    IsBuiltInName(member, "true") ||
                    IsBuiltInName(member, "false");

                if (isBoolLiteral)
                {
                    if (!boolAdded)
                    {
                        result.Add(CheckedTypes.Bool);
                        boolAdded = true;
                    }

                    continue;
                }

                result.Add(member);
            }

            return result;
        }

        private static List<ICheckedType> RemoveSubsumedMembers(
            List<ICheckedType> members,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var result = new List<ICheckedType>();
            foreach (var member in members)
            {
                var subsumed = members.Any(other =>
                    !ReferenceEquals(other, member) &&
                    !AreTypesEqual(other, member) &&
                    IsSubtypeOf(member, other, symbolTree, globalScope));

                if (!subsumed)
                {
                    result.Add(member);
                }
            }

            return result;
        }
    }
}
