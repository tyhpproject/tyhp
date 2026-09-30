using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        private static bool TryCheckIterableAssignability(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited,
            out bool result)
        {
            result = false;

            // Source `iterable` cases. `iterable` is equivalent to `array|\Traversable` (§3.8). It is only
            // assignable to itself (handled earlier by equality) or to a union that covers both halves.
            if (IsIterableType(source))
            {
                if (target is UnionCheckedType unionTarget &&
                    unionTarget.Members.Any(IsArrayLikeType) &&
                    unionTarget.Members.Any(member => IsTraversableType(member, symbolTree, globalScope)))
                {
                    result = AreIterableGenericsCompatible(source, target, symbolTree, globalScope, visited);
                    return true;
                }

                // `iterable` is NOT assignable to `array` alone or `\Traversable` alone (§3.8 rules 4-5).
                if (!IsIterableType(target))
                {
                    result = false;
                    return true;
                }
            }

            if (!IsIterableType(target))
            {
                return false;
            }

            if (IsArrayLikeType(source) && !IsIterableType(source))
            {
                result = AreIterableGenericsCompatible(source, target, symbolTree, globalScope, visited);
                return true;
            }

            var traversable = ResolveTraversable(symbolTree, globalScope);
            if (traversable is not null &&
                TryGetObjectDeclaration(source) is { } sourceObject &&
                ImplementsOrExtends(
                    sourceObject,
                    traversable,
                    symbolTree,
                    globalScope,
                    new HashSet<ObjectDeclarationSymbol>()))
            {
                result = AreIterableGenericsCompatible(source, target, symbolTree, globalScope, visited);
                return true;
            }

            return false;
        }

        private static bool TryCheckCallableAssignability(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited,
            out bool result)
        {
            result = false;

            if (!IsCallableType(target))
            {
                return false;
            }

            if (source is CallableCheckedType)
            {
                return false;
            }

            // Intersection / union of callable facets: any-arity targets check every return;
            // known-arity targets fall through to per-member assignability.
            if (source is IntersectionCheckedType or UnionCheckedType)
            {
                if (TryAsCallableCheckedType(target, out var anyArityTarget)
                    && anyArityTarget.IsAnyArity)
                {
                    var facets = CallableArityFacetBuilder.GetCallableFacets(source);
                    if (facets.Count > 0)
                    {
                        result = facets.All(facet =>
                            IsAssignableToCore(
                                facet.ReturnType,
                                anyArityTarget.ReturnType,
                                symbolTree,
                                globalScope,
                                visited));
                        return true;
                    }
                }

                return false;
            }

            // An unverified plain `string`/`array` is rejected here (most strings are not valid PHP
            // callables), but `\__FunctionName` is a narrower, checker-verified brand: the checker only
            // produces it once a value has passed a `\function_exists(...)` guard, at which point it
            // *is* a name for an existing function — and any string naming an existing function is a
            // valid PHP callable at runtime. Treat it as callable-assignable rather than lumping it in
            // with the general string rejection below.
            if (SymbolNameTypeHelper.TryGetBehavior(source, out var sourceBehavior)
                && sourceBehavior == UtilityBehavior.FunctionName)
            {
                result = true;
                return true;
            }

            if (IsBuiltInName(source, "string") || IsBuiltInName(source, "array"))
            {
                result = false;
                return true;
            }

            // `\Closure<C, TThis, TScope>` is a class; callable-ness is C / `__invoke`.
            // A typed `callable(...)` target must see C, not only the Closure class name —
            // otherwise first-class callables (inferred as Closure generics) fail 4010.
            if (IsClosureType(source, symbolTree, globalScope))
            {
                if (source is GenericCheckedType { TypeArguments.Count: > 0 } genericClosure)
                {
                    result = IsAssignableToCore(
                        genericClosure.TypeArguments[0], target, symbolTree, globalScope, visited);
                    return true;
                }

                result = true;
                return true;
            }

            if (TryAsCallableCheckedType(target, out _))
            {
                // Typed `callable(...)` (including any-arity) must match `__invoke`'s
                // parameters and return, not merely the presence of the method.
                if (TryGetObjectDeclaration(source) is { } obj
                    && TryBuildInvokeCallableType(
                        source, obj, symbolTree, globalScope, out var invokeShape))
                {
                    result = IsAssignableToCore(
                        invokeShape, target, symbolTree, globalScope, visited);
                    return true;
                }

                result = false;
                return true;
            }

            if (TryGetObjectDeclaration(source) is { } invokeObj && HasPublicInvokeMethod(invokeObj, symbolTree))
            {
                result = true;
                return true;
            }

            result = false;
            return true;
        }

        /// <summary>
        /// Structural satisfaction of a <c>struct</c> shape by a source type (§3.2 rule 15). Used when a
        /// struct appears as a member of an intersection target (e.g. <c>object&amp;StructType</c>). The source
        /// must be a struct with compatible properties, or an object declaring every struct property.
        /// </summary>
        private static bool SourceSatisfiesStruct(
            ICheckedType source,
            StructCheckedType structType,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (source is StructCheckedType sourceStruct)
            {
                return IsStructAssignableToStruct(sourceStruct, structType, symbolTree, globalScope, visited);
            }

            if (TryGetObjectDeclaration(source) is { } objectDecl)
            {
                return ObjectSatisfiesStruct(objectDecl, structType, symbolTree, globalScope, visited);
            }

            return false;
        }

        /// <summary>
        /// Width-and-key assignability: every required target property must exist on the source
        /// as a required field with an assignable type. Optional target properties
        /// (<see cref="StructPropertyInfo.IsOptional"/>) may be omitted. A source property that is
        /// itself optional cannot satisfy a required target property — instances of the source may
        /// lack that key. Extra source properties are ignored.
        /// </summary>
        private static bool IsStructAssignableToStruct(
            StructCheckedType source,
            StructCheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            foreach (var (name, targetProperty) in target.Properties)
            {
                if (!source.Properties.TryGetValue(name, out var sourceProperty))
                {
                    if (targetProperty.IsOptional)
                    {
                        continue;
                    }

                    return false;
                }

                // Optional source keys may be absent at runtime, so they cannot fulfill a
                // required target key even when the property types would otherwise match.
                if (!targetProperty.IsOptional && sourceProperty.IsOptional)
                {
                    return false;
                }

                if (!IsAssignableToCore(sourceProperty.Type, targetProperty.Type, symbolTree, globalScope, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsStructAssignableToArray(
            StructCheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (!IsArrayLikeType(target))
            {
                return false;
            }

            if (target is GenericCheckedType { TypeArguments.Count: > 0 } genericArray)
            {
                var keyType = genericArray.TypeArguments[0];
                if (!AreStructKeysAssignableTo(source, keyType))
                {
                    return false;
                }

                if (genericArray.TypeArguments.Count == 2)
                {
                    var valueType = genericArray.TypeArguments[1];
                    var propertyUnion = UnionPropertyTypes(
                        source.Properties.Values.Select(property => property.Type),
                        symbolTree,
                        globalScope);
                    return IsAssignableToCore(propertyUnion, valueType, symbolTree, globalScope, visited);
                }
            }

            return true;
        }

        /// <summary>
        /// Materializes a <see cref="StructCheckedType"/> shape from either an anonymous struct type
        /// or a named struct declaration (<see cref="ObjectDeclarationSymbol.IsStruct"/>).
        /// Uses <see cref="StructTypeHelper"/> so generic property types (<c>T1 0 as $_1</c>)
        /// resolve in the declaring struct's generic scope and inherited properties are included.
        /// </summary>
        internal static StructCheckedType? TryGetStructShapeForAssignability(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (type is StructCheckedType structType)
            {
                return structType;
            }

            // Minimal state: EnclosingObject left null so foreign receivers apply defaults.
            var state = new CheckerState();
            ICheckedType SilentResolve(
                ITypeExpression typeAst,
                CheckerState st,
                bool _isRet,
                bool _isUser) =>
                ResolveTypeAstSilently(typeAst, st, symbolTree, globalScope);

            return StructTypeHelper.TryGetStructShape(type, state, symbolTree, globalScope, SilentResolve);
        }

        /// <summary>
        /// True when <paramref name="type"/> inhabits the built-in <c>struct</c> bound
        /// (<c>T extends struct</c>): anonymous shapes, named struct declarations, and
        /// the built-in itself. Classes and arrays do not.
        /// </summary>
        internal static bool IsStructInhabitant(ICheckedType type)
        {
            if (type is StructCheckedType || IsBuiltInName(type, "struct"))
            {
                return true;
            }

            if (TryGetObjectDeclaration(type) is { IsStruct: true })
            {
                return true;
            }

            if (type is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint }
                })
            {
                return IsStructInhabitant(constraint);
            }

            if (type is IntersectionCheckedType intersection)
            {
                return intersection.Members.Any(IsStructInhabitant);
            }

            return false;
        }

        private static ICheckedType ResolveTypeAstSilently(
            ITypeExpression typeAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            // `extends Parent<T>` and `T 0 as $_1` are ordinary type expressions
            // (`PhpTypeExpressionAst` around a named type). Generic parameters live on the
            // declaring symbol, not the file scope, so the wrapper has to be opened before
            // the in-scope parameter lookup below. Otherwise an inherited field stays the
            // open parent parameter and `CallableArgs2<string, int>` does not match the
            // positional bag.
            if (typeAst is PhpTypeExpressionAst
                {
                    TypeKind: not (PhpTypeKind.Union or PhpTypeKind.Intersection),
                    Types: { } members,
                } composite)
            {
                ITypeExpression? only = null;
                var count = 0;
                foreach (var member in members.GetAllNotNull())
                {
                    count++;
                    if (count > 1)
                    {
                        break;
                    }

                    only = member;
                }

                if (count == 1 && only is not null)
                {
                    var inner = ResolveTypeAstSilently(only, state, symbolTree, globalScope);
                    return composite.IsNullable ? new NullableCheckedType(inner) : inner;
                }
            }

            var simpleName = typeAst switch
            {
                PhpNamedTypeAst { Name: { } name } =>
                    FirstNonEmpty(name.ValueString, name.Identifier),
                PhpBuiltinTypeAst builtin =>
                    FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                PhpNameAst name =>
                    FirstNonEmpty(name.ValueString, name.Identifier),
                _ => null,
            };

            if (simpleName is not null)
            {
                var fromObjectGenerics = state.ObjectGenerics
                    .FirstOrDefault(gp => string.Equals(gp.Name, simpleName, StringComparison.Ordinal));
                if (fromObjectGenerics is not null)
                {
                    return CheckedTypes.FromSymbol(fromObjectGenerics);
                }

                if (state.EnclosingObject is { } enclosing)
                {
                    var fromEnclosing = enclosing.GenericParameters
                        .FirstOrDefault(gp => string.Equals(gp.Name, simpleName, StringComparison.Ordinal));
                    if (fromEnclosing is not null)
                    {
                        return CheckedTypes.FromSymbol(fromEnclosing);
                    }
                }
            }

            var scope = state.NameResolutionScope
                ?? state.EnclosingObject?.ContainingScope
                ?? globalScope;
            var resolved = symbolTree.ResolveType(typeAst, scope, SilentDiagnostics);
            return resolved is null ? CheckedTypes.Unresolved : CheckedTypes.FromSymbol(resolved);
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool IsUntypedArray(ICheckedType type) =>
            IsArrayLikeType(type) &&
            type is not GenericCheckedType { TypeArguments.Count: > 0 };

        private static ICheckedType UnionPropertyTypes(
            IEnumerable<ICheckedType> propertyTypes,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            ICheckedType? result = null;
            foreach (var propertyType in propertyTypes)
            {
                result = result is null
                    ? propertyType
                    : UnionTypesCore([result, propertyType], symbolTree, globalScope);
            }

            return result ?? CheckedTypes.Mixed;
        }

        private static bool ObjectSatisfiesStruct(
            ObjectDeclarationSymbol objectDecl,
            StructCheckedType structType,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            foreach (var (name, structProperty) in structType.Properties)
            {
                var structPropertyType = structProperty.Type;
                var member = symbolTree.ResolveMember(name, objectDecl, SilentDiagnostics);
                if (member is not ObjectPropertySymbol property)
                {
                    if (structProperty.IsOptional)
                    {
                        continue;
                    }

                    return false;
                }

                if (property.DeclaredType is null)
                {
                    return false;
                }

                var scope = objectDecl.ContainingScope ?? globalScope;
                var resolved = symbolTree.ResolveType(property.DeclaredType, scope, SilentDiagnostics);
                if (resolved is null)
                {
                    return false;
                }

                var propertyType = CheckedTypes.FromSymbol(resolved);
                if (!IsAssignableToCore(structPropertyType, propertyType, symbolTree, globalScope, visited))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// <c>callable(__CallableParametersRest&lt;T&gt; ...): __CallableReturnType&lt;T&gt;</c>
        /// is T's signature reconstructed from packs, so it assigns to the callable type
        /// parameter T (e.g. <c>memoize</c>).
        /// </summary>
        private static bool IsReconstructedCallableAssignableTo(ICheckedType source, ICheckedType target)
        {
            if (source is GenericCheckedType sourceGeneric
                && CallableArityFacetBuilder.IsClosureTypeName(sourceGeneric.BaseType)
                && sourceGeneric.TypeArguments.Count > 0)
            {
                source = sourceGeneric.TypeArguments[0];
            }

            var targetShape = target;
            if (target is GenericCheckedType targetGeneric
                && CallableArityFacetBuilder.IsClosureTypeName(targetGeneric.BaseType)
                && targetGeneric.TypeArguments.Count > 0)
            {
                targetShape = targetGeneric.TypeArguments[0];
            }

            if (targetShape is not SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol targetParam
                })
            {
                return false;
            }

            var facets = CallableArityFacetBuilder.GetCallableFacets(source);
            if (facets.Count == 0)
            {
                return false;
            }

            foreach (var facet in facets)
            {
                if (!FacetReconstructsTypeParameter(facet, targetParam))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FacetReconstructsTypeParameter(
            CallableCheckedType facet,
            GenericTypeParameterSymbol param)
        {
            if (!IsReturnTypeOfParameter(facet.ReturnType, param))
            {
                return false;
            }

            var spliced = ParameterPack.SpliceParameterList(facet.ParameterTypes, out _);
            if (spliced.Count != 1)
            {
                return false;
            }

            if (UtilityTypeResolver.TryGetCallableParametersRest(spliced[0], out var restOf)
                && restOf is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol restParam }
                && ReferenceEquals(restParam, param))
            {
                return true;
            }

            return spliced[0] is ParameterPackCheckedType pack
                && pack.SourceCallable is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol packParam
                }
                && ReferenceEquals(packParam, param);
        }

        private static bool IsReturnTypeOfParameter(ICheckedType type, GenericTypeParameterSymbol param)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is not GenericCheckedType generic
                || generic.TypeArguments.Count == 0
                || !SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility)
                || utility.Behavior != UtilityBehavior.CallableReturnType)
            {
                return false;
            }

            return generic.TypeArguments[0] is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol returnParam
                }
                && ReferenceEquals(returnParam, param);
        }

        private static bool AreCallableTypesCompatible(
            CallableCheckedType source,
            CallableCheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (target.IsAnyArity)
            {
                return IsAssignableToCore(
                    source.ReturnType, target.ReturnType, symbolTree, globalScope, visited);
            }

            if (source.IsAnyArity)
            {
                return false;
            }

            var sourceParams = ParameterPack.SpliceParameterList(
                source.ParameterTypes, out var sourcePackVariadic);
            var targetParams = ParameterPack.SpliceParameterList(
                target.ParameterTypes, out var targetPackVariadic);
            var sourceVariadic = source.LastParameterIsVariadic || sourcePackVariadic;
            var targetVariadic = target.LastParameterIsVariadic || targetPackVariadic;
            var sourceLastIsPack = source.ParameterTypes.Count > 0
                && ParameterPack.IsPack(source.ParameterTypes[^1]);
            var targetLastIsPack = target.ParameterTypes.Count > 0
                && ParameterPack.IsPack(target.ParameterTypes[^1]);

            // Value-position `Rest<T> ...$args` and type-position splice `callable(Rest<T> ...): R`
            // are the same callable when T is still open; do not demand the PHP-variadic flag.
            if (targetVariadic && !sourceVariadic && !(sourceLastIsPack && targetLastIsPack))
            {
                return false;
            }

            if (sourceParams.Count != targetParams.Count)
            {
                return false;
            }

            for (var i = 0; i < targetParams.Count; i++)
            {
                if (!IsAssignableToCore(
                        targetParams[i],
                        sourceParams[i],
                        symbolTree,
                        globalScope,
                        visited))
                {
                    return false;
                }
            }

            return IsAssignableToCore(source.ReturnType, target.ReturnType, symbolTree, globalScope, visited);
        }

        private static bool IsArrayLikeType(ICheckedType type) =>
            IsBuiltInName(type, "array") ||
            (type is GenericCheckedType generic && IsBuiltInName(generic.BaseType, "array"));

        private static bool IsIterableType(ICheckedType type) =>
            IsBuiltInName(type, "iterable") ||
            (type is GenericCheckedType generic && IsBuiltInName(generic.BaseType, "iterable"));

        private static bool IsCallableType(ICheckedType type) =>
            type is CallableCheckedType
            || IsBuiltInName(type, "callable");

        /// <summary>
        /// True when <paramref name="type"/> is a known-arity <c>callable(…): R</c> shape
        /// (including an optional-arity intersection of facets). Bare <c>callable</c> and
        /// constraint-only <c>callable(...): R</c> are not shapes.
        /// </summary>
        internal static bool IsCallableShapeType(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is CallableCheckedType { IsAnyArity: false })
            {
                return true;
            }

            if (type is IntersectionCheckedType intersection)
            {
                return intersection.Members.Any(IsCallableShapeType);
            }

            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                && CallableArityFacetBuilder.IsClosureTypeName(generic.BaseType))
            {
                return IsCallableShapeType(generic.TypeArguments[0]);
            }

            return false;
        }

        private static bool IsClosureType(ICheckedType type, SymbolTree symbolTree, GlobalScope globalScope)
        {
            if (TryGetObjectDeclaration(type) is not { } obj)
            {
                return false;
            }

            var closure = ResolveObjectType("Closure", symbolTree, globalScope);
            if (closure is not null && SymbolsMatch(obj, closure))
            {
                return true;
            }

            return string.Equals(obj.Name, "Closure", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(NormalizeFqn(obj), "Closure", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPublicInvokeMethod(ObjectDeclarationSymbol objectDecl, SymbolTree symbolTree) =>
            TryGetInvokeMethod(objectDecl, symbolTree) is not null;

        private static ObjectMethodSymbol? TryGetInvokeMethod(
            ObjectDeclarationSymbol objectDecl,
            SymbolTree symbolTree)
        {
            var member = symbolTree.ResolveMember("__invoke", objectDecl, SilentDiagnostics);
            return member is ObjectMethodSymbol { IsStatic: false } method ? method : null;
        }

        /// <summary>
        /// Builds the object's <c>__invoke</c> signature as a <see cref="CallableCheckedType"/>
        /// (or an intersection of arity siblings when trailing parameters have defaults) so
        /// typed <c>callable(...)</c> assignability can reuse
        /// <see cref="AreCallableTypesCompatible"/>.
        /// </summary>
        private static bool TryBuildInvokeCallableType(
            ICheckedType source,
            ObjectDeclarationSymbol objectDecl,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ICheckedType invokeShape)
        {
            invokeShape = CheckedTypes.Unresolved;
            var method = TryGetInvokeMethod(objectDecl, symbolTree);
            if (method is null)
            {
                return false;
            }

            var declaringClass = FindDeclaringClassForMember(
                objectDecl, method, symbolTree, globalScope) ?? objectDecl;
            var state = new CheckerState
            {
                EnclosingObject = declaringClass,
                EnclosingObjectType = CheckedTypes.FromSymbol(declaringClass),
                NameResolutionScope = declaringClass.ContainingScope,
                ObjectGenerics = declaringClass.GenericParameters.Count > 0
                    ? declaringClass.GenericParameters
                    : [],
            };

            ICheckedType SilentResolve(
                ITypeExpression typeAst,
                CheckerState st,
                bool _isRet,
                bool _isUser) =>
                ResolveTypeAstSilently(typeAst, st, symbolTree, globalScope);

            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (GenericInheritanceBindings.TryBuild(
                    source, state, symbolTree, globalScope, SilentResolve, out var built)
                && built.Count > 0)
            {
                bindings = built;
            }

            ICheckedType Substitute(ICheckedType type) =>
                bindings is null
                    ? type
                    : ResolveGenericTypeBySymbol(type, bindings, symbolTree, globalScope);

            var paramTypes = new List<ICheckedType>(method.Parameters.Count);
            foreach (var param in method.Parameters)
            {
                var resolved = param.DeclaredType is null
                    ? CheckedTypes.Unresolved
                    : SilentResolve(param.DeclaredType, state, false, true);
                paramTypes.Add(Substitute(resolved));
            }

            ICheckedType returnType = method.ReturnType switch
            {
                null => CheckedTypes.Mixed,
                TyhpReturnTypeGuardAst => CheckedTypes.Bool,
                var returnAst => Substitute(SilentResolve(returnAst, state, true, true)),
            };

            invokeShape = CallableArityFacetBuilder.BuildFromParameterInfos(
                method.Parameters, paramTypes, returnType);
            return true;
        }

        private static ObjectDeclarationSymbol? FindDeclaringClassForMember(
            ObjectDeclarationSymbol start,
            ObjectMethodSymbol method,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            ObjectDeclarationSymbol? current = start;
            while (current is not null && visited.Add(current))
            {
                if (current.Members.Values.Any(candidate => ReferenceEquals(candidate, method)))
                {
                    return current;
                }

                current = TryGetParentDeclaration(current, symbolTree, globalScope);
            }

            return start;
        }

        private static ObjectDeclarationSymbol? ResolveTraversable(SymbolTree symbolTree, GlobalScope globalScope) =>
            ResolveObjectType("Traversable", symbolTree, globalScope);

        private static bool IsTraversableType(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var traversable = ResolveTraversable(symbolTree, globalScope);
            return traversable is not null &&
                   TryGetObjectDeclaration(type) is { } obj &&
                   ImplementsOrExtends(obj, traversable, symbolTree, globalScope, new HashSet<ObjectDeclarationSymbol>());
        }

        /// <summary>
        /// A struct erases to a PHP array keyed by its property names, so its keys are strings —
        /// except <c>T 0 as $_1</c> properties (<c>CallableArgs*</c> /
        /// <c>__CallableParametersTuple</c>), which erase to int keys. The struct fits
        /// <c>array&lt;K, V&gt;</c> only when <paramref name="keyType"/> admits every kind of key
        /// it actually emits. The <c>array&lt;V&gt;</c> shorthand normalizes to an
        /// <c>int|string</c> key, which admits both.
        /// </summary>
        private static bool AreStructKeysAssignableTo(StructCheckedType source, ICheckedType keyType)
        {
            var hasIntKeys = false;
            var hasStringKeys = false;
            foreach (var property in source.Properties.Values)
            {
                if (property.IntegerKeyAlias is null)
                {
                    hasStringKeys = true;
                }
                else
                {
                    hasIntKeys = true;
                }
            }

            if (hasStringKeys && IsOnlyArrayKeyOfType(keyType, "int"))
            {
                return false;
            }

            return !hasIntKeys || !IsOnlyArrayKeyOfType(keyType, "string");
        }

        private static bool IsOnlyArrayKeyOfType(ICheckedType keyType, string builtInName)
        {
            if (IsBuiltInName(keyType, builtInName))
            {
                return true;
            }

            if (keyType is UnionCheckedType union)
            {
                return union.Members.All(member => IsBuiltInName(member, builtInName));
            }

            return false;
        }

        private static bool TryAsCallableCheckedType(ICheckedType type, out CallableCheckedType callable)
        {
            if (type is CallableCheckedType direct)
            {
                callable = direct;
                return true;
            }

            callable = null!;
            return false;
        }

        private static bool AreIterableGenericsCompatible(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (source is not GenericCheckedType sourceGeneric || target is not GenericCheckedType targetGeneric)
            {
                return true;
            }

            if (sourceGeneric.TypeArguments.Count != targetGeneric.TypeArguments.Count)
            {
                return sourceGeneric.TypeArguments.Count == 0 || targetGeneric.TypeArguments.Count == 0;
            }

            for (var i = 0; i < sourceGeneric.TypeArguments.Count; i++)
            {
                if (!IsAssignableToCore(
                        sourceGeneric.TypeArguments[i],
                        targetGeneric.TypeArguments[i],
                        symbolTree,
                        globalScope,
                        visited))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
