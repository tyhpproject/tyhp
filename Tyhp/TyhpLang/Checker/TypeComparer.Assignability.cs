using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        private static bool IsAssignableToCore(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (!visited.Add((source, target)))
            {
                return true;
            }

            if (IsUnresolvedType(source) || IsUnresolvedType(target))
            {
                return true;
            }

            if (AreTypesEqualCore(source, target, new HashSet<(ICheckedType, ICheckedType)>()))
            {
                return true;
            }

            // Late-bound `static`: only values already typed as `static` (same declaring class)
            // are assignable. Ordinary `self` / declaring-class instances are not — that keeps
            // `return new self();` from satisfying `: static` without an LSB proof.
            if (target is StaticCheckedType staticTarget)
            {
                return source is StaticCheckedType staticSource
                    && AreTypesEqualCore(
                        staticSource.DeclaringType,
                        staticTarget.DeclaringType,
                        new HashSet<(ICheckedType, ICheckedType)>());
            }

            // `$this` / other `static`-typed values are instances of the declaring class (or a
            // subclass at the call site after expansion), so they satisfy ordinary class targets.
            if (source is StaticCheckedType staticSourceForTarget)
            {
                return IsAssignableToCore(
                    staticSourceForTarget.DeclaringType, target, symbolTree, globalScope, visited);
            }

            // A constrained type parameter is a subtype of its upper bound: `T extends object&S`
            // is assignable wherever that bound is (e.g. to `object`, or to `S`).
            if (source is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint }
                }
                && IsAssignableToCore(constraint, target, symbolTree, globalScope, visited))
            {
                return true;
            }

            if (IsMixedType(target))
            {
                return true;
            }

            if (IsNeverType(source))
            {
                return true;
            }

            // `void` resolves either to the void singleton (inferred, no return) or to a nominal
            // builtin symbol (`\void`, from a declared `: void`); treat the two encodings as equal.
            if (IsVoidType(source) && IsVoidType(target))
            {
                return true;
            }

            if (IsVoidType(source))
            {
                return false;
            }

            // A union source is assignable when every member is. Evaluate this before the
            // mixed/nullable-source handling below for two reasons:
            //   1. A union that includes `null` (e.g. `Foo|Bar|null`) reports itself as nullable,
            //      which would otherwise be rejected against a nullable target after the target's
            //      nullability is unwrapped. Per-member assignment handles `null` against the target.
            //   2. A union that includes `unknown` reports `IsMixed` (unknown is mixed-like), which
            //      would otherwise be rejected outright by the mixed-source guard below. Per-member
            //      assignment treats the `unknown` member as universally assignable.
            if (source is UnionCheckedType earlyUnionSource)
            {
                return earlyUnionSource.Members.All(member =>
                    IsAssignableToCore(member, target, symbolTree, globalScope, visited));
            }

            if (IsMixedType(source))
            {
                // `mixed` assigns only to `mixed`, including through a nullable wrapper (`?mixed`).
                // Without the nullable case, `mixed|null` (union member `mixed` vs `?mixed`) and
                // direct `mixed` → `?mixed` were rejected before nullable unwrapping below.
                return IsMixedType(target)
                    || (target is NullableCheckedType { InnerType: var inner } && IsMixedType(inner));
            }

            if (IsNullLiteral(source) && target.IsNullable)
            {
                return true;
            }

            // Nominal builtin `null` (SimpleCheckedType) is the same type as the null literal —
            // guards historically narrowed via FromSymbol("null"), and declared `null` may be either.
            if ((IsNullLiteral(source) || IsBuiltInName(source, "null"))
                && (IsNullLiteral(target) || IsBuiltInName(target, "null")))
            {
                return true;
            }

            if (source.IsNullable && !target.IsNullable && source is not LiteralCheckedType)
            {
                // `?T` must still be able to reach a union that includes `null` (e.g.
                // `T|Promise|null`). Only reject when the target cannot accept null at all.
                if (target is not UnionCheckedType unionWithNull
                    || !unionWithNull.Members.Any(m =>
                        IsNullLiteral(m) || IsBuiltInName(m, "null")))
                {
                    return false;
                }
            }

            if (target is NullableCheckedType nullableTarget)
            {
                if (IsNullLiteral(source))
                {
                    return true;
                }

                // When the source is itself nullable, the target's `null` already covers it; compare
                // the non-null payloads. Without this, the recursion below would re-enter with a
                // still-nullable source against the non-nullable inner target and be rejected by the
                // nullable-source guard above.
                var nonNullSource = source is NullableCheckedType nullableSource
                    ? nullableSource.InnerType
                    : source;
                return IsAssignableToCore(nonNullSource, nullableTarget.InnerType, symbolTree, globalScope, visited);
            }

            if (TryCheckIterableAssignability(source, target, symbolTree, globalScope, visited, out var iterableResult))
            {
                return iterableResult;
            }

            if (target is UnionCheckedType unionTarget)
            {
                // `?T` against `T|U|null`: both the non-null payload and `null` must be accepted.
                // Matching `?T` against each member fails (`?T`↛`T` via the nullable-source guard,
                // and `?T`↛`null`), so unwrap explicitly.
                if (source is NullableCheckedType nullableSource)
                {
                    var nullAccepted = unionTarget.Members.Any(m =>
                        IsNullLiteral(m) || IsBuiltInName(m, "null"));
                    var innerAccepted = unionTarget.Members.Any(member =>
                        IsAssignableToCore(
                            nullableSource.InnerType, member, symbolTree, globalScope, visited));
                    return nullAccepted && innerAccepted;
                }

                // `bool` ≡ `true|false`: a bool source is covered when the union includes both
                // arms (or an explicit `bool` member). CheckedTypes.UnionTypes usually folds
                // first; this covers any leftover unfolded union (FOUND #42).
                if (IsBuiltInName(source, "bool") && UnionCoversBool(unionTarget))
                {
                    return true;
                }

                return unionTarget.Members.Any(member =>
                    IsAssignableToCore(source, member, symbolTree, globalScope, visited));
            }

            // Intersection on both sides: each target member must be covered by *some* source
            // member, not by one member covering the whole target. Callable arity facets make the
            // difference visible — no single facet satisfies two arities, so a function with three
            // optional-arity facets has to satisfy a two-facet annotation via two of them.
            if (source is IntersectionCheckedType coveringSource
                && target is IntersectionCheckedType coveredTarget)
            {
                return coveredTarget.Members.All(targetMember =>
                    coveringSource.Members.Any(sourceMember =>
                        targetMember is StructCheckedType structTarget
                            ? SourceSatisfiesStruct(
                                sourceMember, structTarget, symbolTree, globalScope, visited)
                            : IsAssignableToCore(
                                sourceMember, targetMember, symbolTree, globalScope, visited)));
            }

            if (source is IntersectionCheckedType intersectionSource)
            {
                return intersectionSource.Members.Any(member =>
                    IsAssignableToCore(member, target, symbolTree, globalScope, visited));
            }

            if (target is IntersectionCheckedType intersectionTarget)
            {
                // Struct members are checked structurally (§3.2 rule 15): the source object/struct must
                // declare every property of the struct shape. Non-struct members use nominal assignability.
                return intersectionTarget.Members.All(member =>
                    member is StructCheckedType structMember
                        ? SourceSatisfiesStruct(source, structMember, symbolTree, globalScope, visited)
                        : IsAssignableToCore(source, member, symbolTree, globalScope, visited));
            }

            if (source is LiteralCheckedType literalSource)
            {
                if (SymbolNameTypeHelper.IsSymbolNameType(target))
                {
                    return false;
                }

                if (TryCheckTemplateStringAssignability(
                        source, target, symbolTree, globalScope, visited, out var templateResult))
                {
                    return templateResult;
                }

                // Bool literals are assignable to bool and to the matching true/false type
                // (declared `: true` / `true $x` resolve as BuiltInTypeSymbol("true"), while
                // expression `true` may still carry underlying "bool"). Mirror Subtyping.
                if (literalSource.Value is bool boolValue)
                {
                    if (IsBuiltInName(target, "bool"))
                    {
                        return true;
                    }

                    if (boolValue && (IsBuiltInName(target, "true") || IsBoolLiteralValue(target, true)))
                    {
                        return true;
                    }

                    if (!boolValue && (IsBuiltInName(target, "false") || IsBoolLiteralValue(target, false)))
                    {
                        return true;
                    }
                }

                return IsAssignableToCore(
                    literalSource.UnderlyingType,
                    target,
                    symbolTree,
                    globalScope,
                    visited);
            }

            // Nominal true/false (and literal-typed true/false that fell through above as
            // SimpleCheckedType) are subtypes of bool — assignability must agree with Subtyping.
            if ((IsBuiltInName(source, "true") || IsBuiltInName(source, "false"))
                && IsBuiltInName(target, "bool"))
            {
                return true;
            }

            // The reverse of the literal-source bool-literal carve-out above: a bare nominal
            // `true`/`false` source must also satisfy a *literal-typed* `true`/`false` target.
            // `true`/`false` used as a generic type argument (e.g. `array<true>`) resolves to
            // `LiteralCheckedType(true, Simple("true"))`, while the equivalent array *value* type
            // is widened to the bare nominal `Simple("true")` (`InferArrayLiteral` / `WidenLiteral`
            // deliberately drop literal-ness so `[1, 2, 3]` infers `array<int>`, not a literal
            // union). Both forms denote the exact same single-value type, so `array<true>` must
            // still accept `[true, true]` — without this, only the source-is-literal direction
            // round-tripped and nested generic positions (array/iterable covariance) rejected the
            // widened form.
            if (target is LiteralCheckedType { Value: bool targetBoolValue }
                && IsBuiltInName(source, targetBoolValue ? "true" : "false"))
            {
                return true;
            }

            if (IsBuiltInName(source, "int") && IsBuiltInName(target, "float"))
            {
                return true;
            }

            if (TryCheckCallableAssignability(source, target, symbolTree, globalScope, visited, out var callableResult))
            {
                return callableResult;
            }

            if (source is CallableCheckedType sourceCallable &&
                TryAsCallableCheckedType(target, out var targetCallable))
            {
                return AreCallableTypesCompatible(
                    sourceCallable,
                    targetCallable,
                    symbolTree,
                    globalScope,
                    visited);
            }

            // A callable signature type originates from a closure/arrow-function literal (e.g.
            // `fn() => ...` or `function () { ... }`), which is always an instance of `\Closure`
            // in PHP. Allow such values to satisfy a nominal `\Closure` target so that returning or
            // assigning a closure literal where `\Closure` is expected type-checks.
            if (source is CallableCheckedType && IsClosureType(target, symbolTree, globalScope))
            {
                return true;
            }

            if (source is StructCheckedType sourceStruct && target is StructCheckedType targetStruct)
            {
                return IsStructAssignableToStruct(sourceStruct, targetStruct, symbolTree, globalScope, visited);
            }

            // Struct shapes and named struct declarations erase to PHP arrays. Accept them wherever
            // an array-like target is expected (bare `array` or `array<K, V>` with compatible keys/values).
            if (IsArrayLikeType(target) &&
                TryGetStructShapeForAssignability(source, symbolTree, globalScope) is { } structShape)
            {
                return IsStructAssignableToArray(structShape, target, symbolTree, globalScope, visited);
            }

            // Bare `array` is the gradual any-array: assignable to and from `array<K, V>` /
            // `array<V>`. Typed↔typed still requires argument compatibility below.
            if (IsArrayLikeType(source) && IsArrayLikeType(target) &&
                (IsUntypedArray(source) || IsUntypedArray(target)))
            {
                return true;
            }

            // Same generic declaration: type arguments decide assignability (user generics are
            // invariant, with a G<T>→G<mixed> carve-out excluding void/never; `array`/`iterable`
            // are covariant inside AreGenericArgumentsCompatible).
            // Do not fall through to IsSubtypeOfCore — that path only compares declarations and
            // would accept Box<string> where Box<int> is required (FOUND generic structs 2026-08-05 §1).
            if (source is GenericCheckedType sourceGeneric && target is GenericCheckedType targetGeneric)
            {
                if (AreGenericBasesEqual(sourceGeneric, targetGeneric))
                {
                    if (AreGenericArgumentsCompatible(
                            sourceGeneric,
                            targetGeneric,
                            symbolTree,
                            globalScope,
                            visited,
                            forAssignability: true))
                    {
                        return true;
                    }

                    // Symbol-name brands (`__ClassName<T>`, …): allow erasure widening
                    // (`__ClassName<User>` → `__ClassName<object>`) but not A→B for distinct T.
                    if (SymbolNameTypeHelper.IsSymbolNameType(source)
                        && SymbolNameTypeHelper.IsErasureAssignable(source, target, globalScope))
                    {
                        return true;
                    }

                    // `__CompatibleTypeName<T>` is covariant in T (subclass-as-class-string).
                    // Same-base early return would otherwise block CompatibleTypeName<Dog> →
                    // CompatibleTypeName<Animal> under ordinary generic invariance.
                    if (SymbolNameTypeHelper.IsCompatibleBrandAssignable(
                            source, target, symbolTree, globalScope))
                    {
                        return true;
                    }

                    return false;
                }
            }

            // Named struct declarations erase to PHP arrays and use structural (schema) typing,
            // including against anonymous / synthetic shapes (`__CallableParametersTuple`, inline
            // `struct { … }`) and other named structs. Same-declaration generics already returned
            // above (`Box<string>` ↛ `Box<int>`). Classes are not structs — do not use
            // `ObjectSatisfiesStruct` here (that path would accept objects where an array is required).
            if ((source is StructCheckedType || target is StructCheckedType
                    || TryGetObjectDeclaration(source) is { IsStruct: true }
                    || TryGetObjectDeclaration(target) is { IsStruct: true })
                && TryGetStructShapeForAssignability(source, symbolTree, globalScope) is { } namedSourceShape
                && TryGetStructShapeForAssignability(target, symbolTree, globalScope) is { } namedTargetShape)
            {
                return IsStructAssignableToStruct(
                    namedSourceShape, namedTargetShape, symbolTree, globalScope, visited);
            }

            if (TryGetObjectDeclaration(source) is not null &&
                IsSubtypeOfCore(source, target, symbolTree, globalScope, new HashSet<(ICheckedType, ICheckedType)>()))
            {
                return true;
            }

            if (SymbolNameTypeHelper.IsSymbolNameType(source))
            {
                if (SymbolNameTypeHelper.IsErasureAssignable(source, target, globalScope))
                {
                    return true;
                }

                // Cross-brand: `__ClassName<Dog>` / `__EnumName` / `__InterfaceName` →
                // `__CompatibleTypeName<Animal>` when the named type is a subtype of the brand arg.
                if (SymbolNameTypeHelper.IsCompatibleBrandAssignable(
                        source, target, symbolTree, globalScope))
                {
                    return true;
                }

                var erasure = SymbolNameTypeHelper.GetFullErasure(source, globalScope);
                if (IsAssignableToCore(erasure, target, symbolTree, globalScope, visited))
                {
                    return true;
                }
            }

            if (TryCheckTemplateStringAssignability(
                    source, target, symbolTree, globalScope, visited, out var templateAssignability))
            {
                return templateAssignability;
            }

            if (Rules.CheckerHelpers.IsBuiltInName(source, "string")
                && source is not LiteralCheckedType
                && SymbolNameTypeHelper.IsSymbolNameType(target))
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="type"/> is a bool literal checked type with the given value
        /// (used so <c>LiteralCheckedType(true, …)</c> matches a declared <c>true</c> target that
        /// was itself resolved as a literal type).
        /// </summary>
        private static bool IsBoolLiteralValue(ICheckedType type, bool value) =>
            type is LiteralCheckedType { Value: bool b } && b == value;

        /// <summary>
        /// True when a union already includes <c>bool</c>, or both <c>true</c> and <c>false</c>
        /// (literal or nominal) — i.e. it covers every <c>bool</c> value.
        /// </summary>
        private static bool UnionCoversBool(UnionCheckedType union)
        {
            if (union.Members.Any(m => IsBuiltInName(m, "bool")))
            {
                return true;
            }

            var hasTrue = union.Members.Any(m =>
                IsBuiltInName(m, "true") || IsBoolLiteralValue(m, true));
            var hasFalse = union.Members.Any(m =>
                IsBuiltInName(m, "false") || IsBoolLiteralValue(m, false));
            return hasTrue && hasFalse;
        }
    }
}
