using System.Globalization;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Resolves <c>__SuperType</c>, <c>__SuperTypeName</c>, <c>__CurrentScope</c>,
    /// <c>__CallableThis</c>, <c>__CallableScope</c>, <c>__IndexKeys</c>,
    /// <c>__IndexValueType</c>, and <c>__IndexValueTypes</c>.
    /// </summary>
    /// <remarks>
    /// <c>__CallableThis</c> / <c>__CallableScope</c> treat <c>\Closure</c> as
    /// <c>TCallableShape, TThis, TScope</c> (not return-last). If Closure type arguments
    /// are still rewritten to a callable facet before this resolver runs, those cases
    /// fall through to the bare-<c>callable</c> defaults.
    /// <c>__SuperTypeName&lt;T&gt;</c> stays a symbol-name brand (inverse of
    /// <c>__CompatibleTypeName</c>) so class-name literals assign through existence
    /// verification. When <c>T</c> is <c>object</c> or <c>null</c> it resolves to
    /// <c>__ClassName</c>.
    /// </remarks>
    internal static class MagicUtilityTypeResolver
    {
        private static readonly DiagnosticBag SilentDiagnostics = new();

        public static bool IsMagicBehavior(UtilityBehavior behavior) =>
            behavior is UtilityBehavior.SuperType
                or UtilityBehavior.SuperTypeName
                or UtilityBehavior.CurrentScope
                or UtilityBehavior.CallableThis
                or UtilityBehavior.CallableScope
                or UtilityBehavior.IndexKeys
                or UtilityBehavior.IndexValueType
                or UtilityBehavior.IndexValueTypes;

        /// <summary>
        /// Deferred <c>__SuperType&lt;T&gt;</c> always inhabits <c>object</c>: expansion is
        /// <c>T</c> plus parents, or <c>object</c> when <c>T</c> has no parent /
        /// is <c>object</c>/<c>null</c>. The wrapper is kept while <c>T</c> is unbound
        /// so call-site substitution can still expand the parent chain.
        /// </summary>
        public static bool IsSuperTypeUtility(ICheckedType type) =>
            SymbolNameTypeHelper.TryGetUtilitySymbol(type, out var utility)
            && utility.Behavior == UtilityBehavior.SuperType;

        public static ICheckedType Resolve(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            return utility.Behavior switch
            {
                UtilityBehavior.SuperType =>
                    ResolveSuperType(typeArguments, utility, symbolTree, globalScope),
                UtilityBehavior.SuperTypeName =>
                    ResolveSuperTypeName(typeArguments, utility, symbolTree, globalScope),
                UtilityBehavior.CurrentScope =>
                    ResolveCurrentScope(state),
                UtilityBehavior.CallableThis =>
                    ResolveCallableSlot(utility, typeArguments, symbolTree, globalScope, thisSlot: true),
                UtilityBehavior.CallableScope =>
                    ResolveCallableSlot(utility, typeArguments, symbolTree, globalScope, thisSlot: false),
                UtilityBehavior.IndexKeys =>
                    ResolveIndexKeys(utility, typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.IndexValueType =>
                    ResolveIndexValueType(utility, typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.IndexValueTypes =>
                    ResolveIndexValueTypes(utility, typeArguments, state, symbolTree, globalScope, resolveType),
                _ => CheckedTypes.Unresolved,
            };
        }

        internal static ICheckedType IndexKeysOf(
            ICheckedType structType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var keys = CollectIndexKeyLiterals(structType, state, symbolTree, globalScope, resolveType);
            return keys.Count switch
            {
                0 => CheckedTypes.Unresolved,
                1 => keys[0],
                _ => CheckedTypes.UnionTypes(keys),
            };
        }

        internal static ICheckedType IndexValueTypeOf(
            ICheckedType structType,
            ICheckedType keyType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType) =>
            LookupIndexValueType(structType, keyType, state, symbolTree, globalScope, resolveType);

        internal static ICheckedType IndexValueTypesOf(
            ICheckedType structType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var values = CollectIndexFieldTypes(structType, state, symbolTree, globalScope, resolveType);
            return values.Count switch
            {
                0 => CheckedTypes.Unresolved,
                1 => values[0],
                _ => CheckedTypes.UnionTypes(values),
            };
        }

        public static ICheckedType ExpandAfterSubstitution(
            GenericCheckedType generic,
            BuiltInUtilityTypeSymbol utility,
            SymbolTree? symbolTree,
            GlobalScope? globalScope)
        {
            if (globalScope is null)
            {
                return generic;
            }

            var tree = symbolTree ?? new SymbolTree(globalScope);
            return Resolve(
                utility,
                generic.TypeArguments,
                new CheckerState(),
                tree,
                globalScope,
                ResolveTypeFromBoundSymbol);
        }

        private static ICheckedType ResolveTypeFromBoundSymbol(
            ITypeExpression typeExpr,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            _ = state;
            _ = isReturnTypePosition;
            _ = isUserTypeDeclaration;
            if (typeExpr.BoundSymbol is BuiltInTypeSymbol builtin)
            {
                return CheckedTypes.FromSymbol(builtin);
            }

            if (typeExpr.BoundSymbol is IBaseSymbol symbol)
            {
                return CheckedTypes.FromSymbol(symbol);
            }

            return CheckedTypes.Mixed;
        }

        internal static ICheckedType ResolveCurrentScope(CheckerState state)
        {
            if (state.EnclosingObjectType is { } objectType
                && !TypeComparer.IsUnresolvedType(objectType))
            {
                return objectType;
            }

            return state.EnclosingObject is { } enclosing
                ? CheckedTypes.FromSymbol(enclosing)
                : CheckedTypes.Null;
        }

        private static ICheckedType ResolveSuperType(
            IReadOnlyList<ICheckedType> typeArguments,
            BuiltInUtilityTypeSymbol utility,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (typeArguments.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (ShouldKeepDeferred(typeArguments[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
            }

            return ResolveSuperTypeCore(typeArguments[0], symbolTree, globalScope);
        }

        private static ICheckedType ResolveSuperTypeName(
            IReadOnlyList<ICheckedType> typeArguments,
            BuiltInUtilityTypeSymbol utility,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (typeArguments.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (ShouldKeepDeferred(typeArguments[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
            }

            return ResolveSuperTypeNameCore(typeArguments[0], symbolTree, globalScope);
        }

        private static ICheckedType ResolveSuperTypeCore(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            type = UnwrapNullable(type);
            if (ShouldKeepDeferred(type))
            {
                return type;
            }

            if (type is UnionCheckedType union)
            {
                if (IsNullOrObjectUnion(union))
                {
                    return GetObjectType(globalScope);
                }

                return CheckedTypes.UnionTypes(
                    union.Members.Select(m => ResolveSuperTypeCore(m, symbolTree, globalScope)).ToList());
            }

            if (type is IntersectionCheckedType intersection)
            {
                return intersection.Members
                    .Select(m => ResolveSuperTypeCore(m, symbolTree, globalScope))
                    .Aggregate((a, b) => TypeComparer.IntersectTypes(a, b, symbolTree, globalScope));
            }

            if (IsNullType(type) || IsObjectBuiltin(type))
            {
                return GetObjectType(globalScope);
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is not { } obj)
            {
                return CheckedTypes.Unresolved;
            }

            var members = new List<ICheckedType> { type };
            var current = obj;
            var visited = new HashSet<ObjectDeclarationSymbol> { current };
            while (TypeComparer.TryGetParentDeclaration(current, symbolTree, globalScope) is { } parent
                   && visited.Add(parent))
            {
                members.Add(CheckedTypes.FromSymbol(parent));
                current = parent;
            }

            return members.Count == 1 ? members[0] : CheckedTypes.UnionTypes(members);
        }

        private static ICheckedType ResolveSuperTypeNameCore(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            type = UnwrapNullable(type);
            if (ShouldKeepDeferred(type))
            {
                return type;
            }

            if (type is UnionCheckedType union)
            {
                if (IsNullOrObjectUnion(union))
                {
                    return MakeClassName(globalScope);
                }

                return CheckedTypes.UnionTypes(
                    union.Members.Select(m => ResolveSuperTypeNameCore(m, symbolTree, globalScope)).ToList());
            }

            if (type is IntersectionCheckedType intersection)
            {
                return intersection.Members
                    .Select(m => ResolveSuperTypeNameCore(m, symbolTree, globalScope))
                    .Aggregate((a, b) => TypeComparer.IntersectTypes(a, b, symbolTree, globalScope));
            }

            if (IsNullType(type) || IsObjectBuiltin(type))
            {
                return MakeClassName(globalScope);
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is null)
            {
                return CheckedTypes.Unresolved;
            }

            return SymbolNameTypeHelper.MakeSymbolNameType(
                UtilityBehavior.SuperTypeName, globalScope, [type]);
        }

        private static ICheckedType ResolveCallableSlot(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool thisSlot)
        {
            if (typeArguments.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var callable = typeArguments[0];
            if (ShouldKeepDeferred(callable))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
            }

            return thisSlot
                ? ResolveCallableThisCore(callable, symbolTree, globalScope)
                : ResolveCallableScopeCore(callable, symbolTree, globalScope);
        }

        internal static ICheckedType ResolveCallableThisCore(
            ICheckedType callable,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            callable = UnwrapNullable(callable);
            if (callable is UnionCheckedType union)
            {
                return CheckedTypes.UnionTypes(
                    union.Members.Select(m => ResolveCallableThisCore(m, symbolTree, globalScope)).ToList());
            }

            if (callable is IntersectionCheckedType intersection)
            {
                return intersection.Members
                    .Select(m => ResolveCallableThisCore(m, symbolTree, globalScope))
                    .Aggregate((a, b) => TypeComparer.IntersectTypes(a, b, symbolTree, globalScope));
            }

            if (TryGetClosureTypeArguments(callable, out var closureArgs))
            {
                return closureArgs.Count >= 2 ? closureArgs[1] : ObjectOrNull(globalScope);
            }

            if (TryGetInvokableObject(callable, symbolTree, out var invokable))
            {
                return invokable;
            }

            return ObjectOrNull(globalScope);
        }

        internal static ICheckedType ResolveCallableScopeCore(
            ICheckedType callable,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            callable = UnwrapNullable(callable);
            if (callable is UnionCheckedType union)
            {
                return CheckedTypes.UnionTypes(
                    union.Members.Select(m => ResolveCallableScopeCore(m, symbolTree, globalScope)).ToList());
            }

            if (callable is IntersectionCheckedType intersection)
            {
                return intersection.Members
                    .Select(m => ResolveCallableScopeCore(m, symbolTree, globalScope))
                    .Aggregate((a, b) => TypeComparer.IntersectTypes(a, b, symbolTree, globalScope));
            }

            if (TryGetClosureTypeArguments(callable, out var closureArgs))
            {
                if (closureArgs.Count >= 3)
                {
                    return closureArgs[2];
                }

                var thisType = closureArgs.Count >= 2 ? closureArgs[1] : ObjectOrNull(globalScope);
                return DefaultClosureScope(thisType, symbolTree, globalScope);
            }

            if (TryGetInvokableObject(callable, symbolTree, out var invokable))
            {
                return invokable;
            }

            return DefaultClosureScope(ObjectOrNull(globalScope), symbolTree, globalScope);
        }

        private static ICheckedType DefaultClosureScope(
            ICheckedType thisType,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            CheckedTypes.UnionTypes(
            [
                ResolveSuperTypeCore(thisType, symbolTree, globalScope),
                ResolveSuperTypeNameCore(thisType, symbolTree, globalScope),
                CheckedTypes.Null,
            ]);

        private static bool TryGetClosureTypeArguments(
            ICheckedType type,
            out IReadOnlyList<ICheckedType> typeArguments)
        {
            typeArguments = [];
            if (type is GenericCheckedType generic && IsClosureType(generic.BaseType))
            {
                typeArguments = generic.TypeArguments;
                return true;
            }

            if (IsClosureType(type))
            {
                return true;
            }

            return false;
        }

        private static bool IsClosureType(ICheckedType type)
        {
            if (type is GenericCheckedType generic)
            {
                type = generic.BaseType;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is { } obj)
            {
                var fqn = obj.FullyQualifiedName.TrimStart('\\');
                return string.Equals(obj.Name, "Closure", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fqn, "Closure", StringComparison.OrdinalIgnoreCase);
            }

            return CallableArityFacetBuilder.IsClosureTypeName(type);
        }

        private static bool TryGetInvokableObject(
            ICheckedType type,
            SymbolTree symbolTree,
            out ICheckedType objectType)
        {
            objectType = type;
            if (IsClosureType(type) || CheckerHelpers.TryGetObjectDeclaration(type) is not { } obj)
            {
                return false;
            }

            var member = symbolTree.ResolveMember("__invoke", obj, SilentDiagnostics);
            if (member is not ObjectMethodSymbol { IsStatic: false })
            {
                return false;
            }

            objectType = type;
            return true;
        }

        private static ICheckedType ResolveIndexKeys(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (typeArguments.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (ShouldKeepDeferred(typeArguments[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
            }

            var keys = CollectIndexKeyLiterals(typeArguments[0], state, symbolTree, globalScope, resolveType);
            return keys.Count switch
            {
                0 => CheckedTypes.Unresolved,
                1 => keys[0],
                _ => CheckedTypes.UnionTypes(keys),
            };
        }

        private static ICheckedType ResolveIndexValueType(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (typeArguments.Count < 2)
            {
                return CheckedTypes.Unresolved;
            }

            if (ShouldKeepDeferred(typeArguments[0]) || ShouldKeepDeferred(typeArguments[1]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
            }

            return LookupIndexValueType(
                typeArguments[0], typeArguments[1], state, symbolTree, globalScope, resolveType);
        }

        private static ICheckedType ResolveIndexValueTypes(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (typeArguments.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (ShouldKeepDeferred(typeArguments[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
            }

            var values = CollectIndexFieldTypes(typeArguments[0], state, symbolTree, globalScope, resolveType);
            return values.Count switch
            {
                0 => CheckedTypes.Unresolved,
                1 => values[0],
                _ => CheckedTypes.UnionTypes(values),
            };
        }

        private static ICheckedType LookupIndexValueType(
            ICheckedType structType,
            ICheckedType keyType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            keyType = UnwrapNullable(keyType);
            if (keyType is UnionCheckedType union)
            {
                return CheckedTypes.UnionTypes(
                    union.Members
                        .Select(m => LookupIndexValueType(structType, m, state, symbolTree, globalScope, resolveType))
                        .ToList());
            }

            foreach (var field in CollectIndexFields(structType, state, symbolTree, globalScope, resolveType))
            {
                if (KeyMatches(keyType, field))
                {
                    return field.Type;
                }
            }

            return CheckedTypes.Unresolved;
        }

        private sealed record IndexField(ICheckedType Type, IReadOnlyList<ICheckedType> Keys);

        private static List<ICheckedType> CollectIndexKeyLiterals(
            ICheckedType structType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var keys = new List<ICheckedType>();
            foreach (var field in CollectIndexFields(structType, state, symbolTree, globalScope, resolveType))
            {
                foreach (var key in field.Keys)
                {
                    if (seen.Add(KeyIdentity(key)))
                    {
                        keys.Add(key);
                    }
                }
            }

            return keys;
        }

        private static List<ICheckedType> CollectIndexFieldTypes(
            ICheckedType structType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var types = new List<ICheckedType>();
            foreach (var field in CollectIndexFields(structType, state, symbolTree, globalScope, resolveType))
            {
                if (!types.Any(existing => CheckedTypes.AreTypesEqual(existing, field.Type)))
                {
                    types.Add(field.Type);
                }
            }

            return types;
        }

        private static List<IndexField> CollectIndexFields(
            ICheckedType structType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (CheckerHelpers.TryGetObjectDeclaration(structType) is { IsStruct: true } obj)
            {
                var byName = new Dictionary<string, IndexField>(StringComparer.Ordinal);
                CollectFieldsFromObject(
                    obj, state, symbolTree, globalScope, resolveType, byName, new HashSet<ObjectDeclarationSymbol>());
                return byName.Values.ToList();
            }

            var shape = StructTypeHelper.TryGetStructShape(
                structType, state, symbolTree, globalScope, resolveType);
            if (shape is null)
            {
                return [];
            }

            var fields = new List<IndexField>(shape.Properties.Count);
            foreach (var (name, info) in shape.Properties)
            {
                var keys = new List<ICheckedType> { MakeStringLiteral(NormalizePropertyKey(name)) };
                if (info.IntegerKeyAlias is int intKey)
                {
                    keys.Add(MakeIntLiteral(intKey));
                }

                fields.Add(new IndexField(info.Type, keys));
            }

            return fields;
        }

        private static void CollectFieldsFromObject(
            ObjectDeclarationSymbol obj,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            Dictionary<string, IndexField> fields,
            HashSet<ObjectDeclarationSymbol> visited)
        {
            if (!visited.Add(obj))
            {
                return;
            }

            if (TypeComparer.TryGetParentDeclaration(obj, symbolTree, globalScope) is { } parent)
            {
                CollectFieldsFromObject(parent, state, symbolTree, globalScope, resolveType, fields, visited);
            }

            var shape = StructTypeHelper.TryGetStructShape(
                CheckedTypes.FromSymbol(obj), state, symbolTree, globalScope, resolveType);

            foreach (var member in obj.Members.Values)
            {
                if (member is not ObjectPropertySymbol property)
                {
                    continue;
                }

                var type = shape is not null && shape.Properties.TryGetValue(member.Name, out var info)
                    ? info.Type
                    : property.DeclaredType is { } declared
                        ? resolveType(declared, state, false, true)
                        : CheckedTypes.Mixed;

                fields[member.Name] = new IndexField(type, CollectKeysForProperty(property));
            }
        }

        private static List<ICheckedType> CollectKeysForProperty(ObjectPropertySymbol property)
        {
            var keys = new List<ICheckedType>
            {
                MakeStringLiteral(NormalizePropertyKey(property.Name)),
            };

            if (property.DeclaringAstNode is not TyhpStructPropertyAst structProp
                || string.IsNullOrEmpty(structProp.AliasOf))
            {
                if (StructTypeHelper.TryGetIntegerKeyAlias(property) is int intAlias)
                {
                    keys.Add(MakeIntLiteral(intAlias));
                }

                return keys;
            }

            if (structProp.IsNumericAlias
                && structProp.ValueInt64 is long numeric
                && numeric is >= int.MinValue and <= int.MaxValue)
            {
                keys.Add(MakeIntLiteral((int)numeric));
            }
            else
            {
                var alias = UnquotePhpStringLiteral(structProp.AliasOf);
                if (!string.IsNullOrEmpty(alias)
                    && !string.Equals(alias, NormalizePropertyKey(property.Name), StringComparison.Ordinal))
                {
                    keys.Add(MakeStringLiteral(alias));
                }
            }

            return keys;
        }

        private static bool KeyMatches(ICheckedType keyType, IndexField field)
        {
            foreach (var key in field.Keys)
            {
                if (CheckedTypes.AreTypesEqual(keyType, key))
                {
                    return true;
                }

                if (keyType is LiteralCheckedType keyLiteral
                    && key is LiteralCheckedType fieldLiteral
                    && LiteralValuesMatch(keyLiteral.Value, fieldLiteral.Value))
                {
                    return true;
                }
            }

            if (keyType is LiteralCheckedType { Value: string name })
            {
                var normalized = NormalizePropertyKey(name);
                foreach (var key in field.Keys)
                {
                    if (key is LiteralCheckedType { Value: string fieldName }
                        && string.Equals(NormalizePropertyKey(fieldName), normalized, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool LiteralValuesMatch(object? left, object? right)
        {
            if (Equals(left, right))
            {
                return true;
            }

            return TryToInt64(left, out var leftInt) && TryToInt64(right, out var rightInt) && leftInt == rightInt;
        }

        private static bool TryToInt64(object? value, out long result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static string KeyIdentity(ICheckedType key) =>
            key is LiteralCheckedType literal
                ? literal.Value switch
                {
                    string s => "s:" + s,
                    int i => "i:" + i.ToString(CultureInfo.InvariantCulture),
                    long l => "i:" + l.ToString(CultureInfo.InvariantCulture),
                    _ => literal.DisplayName,
                }
                : key.DisplayName;

        private static ICheckedType MakeStringLiteral(string value) =>
            new LiteralCheckedType(value, new SimpleCheckedType(new BuiltInTypeSymbol("string")));

        private static ICheckedType MakeIntLiteral(int value) =>
            new LiteralCheckedType(value, new SimpleCheckedType(new BuiltInTypeSymbol("int")));

        private static string NormalizePropertyKey(string propertyName) =>
            propertyName.StartsWith('$') ? propertyName[1..] : propertyName;

        private static string UnquotePhpStringLiteral(string literal)
        {
            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'')
                    || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1].Replace("\\'", "'").Replace("\\\\", "\\");
            }

            return literal;
        }

        private static ICheckedType MakeClassName(GlobalScope globalScope) =>
            SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.ClassName, globalScope);

        private static ICheckedType ObjectOrNull(GlobalScope globalScope) =>
            CheckedTypes.UnionTypes(GetObjectType(globalScope), CheckedTypes.Null);

        private static ICheckedType GetObjectType(GlobalScope globalScope)
        {
            var symbol = ((IBaseScope)globalScope).FindChildSymbolByName("object") as BuiltInTypeSymbol
                ?? new BuiltInTypeSymbol("object");
            return CheckedTypes.FromSymbol(symbol);
        }

        private static bool IsNullOrObjectUnion(UnionCheckedType union) =>
            union.Members.Count > 0 && union.Members.All(m => IsNullType(m) || IsObjectBuiltin(m));

        private static bool IsNullType(ICheckedType type) =>
            type is LiteralCheckedType { Value: null } || CheckerHelpers.IsBuiltInName(type, "null");

        private static bool IsObjectBuiltin(ICheckedType type) =>
            CheckerHelpers.IsBuiltInName(type, "object");

        private static ICheckedType UnwrapNullable(ICheckedType type) =>
            type is NullableCheckedType nullable
                ? CheckedTypes.UnionTypes(nullable.InnerType, CheckedTypes.Null)
                : type;

        private static bool ShouldKeepDeferred(ICheckedType type) =>
            CallableSignatureReflection.IsUnboundTypeParameter(type)
            || TypeComparer.IsUnresolvedType(type);
    }
}
