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

            if (TryAsCallableCheckedType(target, out _))
            {
                if (IsClosureType(source, symbolTree, globalScope) ||
                    (TryGetObjectDeclaration(source) is { } obj && HasPublicInvokeMethod(obj, symbolTree)))
                {
                    result = true;
                    return true;
                }

                result = false;
                return true;
            }

            if (IsClosureType(source, symbolTree, globalScope))
            {
                result = true;
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

        private static ICheckedType ResolveTypeAstSilently(
            ITypeExpression typeAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
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

        private static bool AreCallableTypesCompatible(
            CallableCheckedType source,
            CallableCheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (target.ParameterTypes.Count == 0)
            {
                return IsAssignableToCore(source.ReturnType, target.ReturnType, symbolTree, globalScope, visited);
            }

            if (source.ParameterTypes.Count != target.ParameterTypes.Count)
            {
                return false;
            }

            for (var i = 0; i < target.ParameterTypes.Count; i++)
            {
                if (!IsAssignableToCore(
                        target.ParameterTypes[i],
                        source.ParameterTypes[i],
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
            IsBuiltInName(type, "callable") ||
            (type is GenericCheckedType generic && IsBuiltInName(generic.BaseType, "callable"));

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

        private static bool HasPublicInvokeMethod(ObjectDeclarationSymbol objectDecl, SymbolTree symbolTree)
        {
            var member = symbolTree.ResolveMember("__invoke", objectDecl, SilentDiagnostics);
            return member is ObjectMethodSymbol { IsStatic: false };
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

            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic &&
                IsBuiltInName(generic.BaseType, "callable"))
            {
                var returnType = generic.TypeArguments[^1];
                var parameterTypes = generic.TypeArguments.Take(generic.TypeArguments.Count - 1).ToList();
                callable = new CallableCheckedType(parameterTypes, returnType);
                return true;
            }

            if (IsBuiltInName(type, "callable"))
            {
                callable = new CallableCheckedType([], CheckedTypes.Mixed);
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
