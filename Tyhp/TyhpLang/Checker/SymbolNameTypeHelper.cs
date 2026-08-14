using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Helpers for Story 08.5 symbol-name types: identification, construction, and erasure chains.
    /// </summary>
    internal static class SymbolNameTypeHelper
    {
        private static readonly IReadOnlyDictionary<UtilityBehavior, string> BehaviorNames = new Dictionary<UtilityBehavior, string>
        {
            [UtilityBehavior.TyhpInternal] = "__TyhpInternal",
            [UtilityBehavior.VarName] = "__VarName",
            [UtilityBehavior.TypedVarName] = "__TypedVarName",
            [UtilityBehavior.FunctionName] = "__FunctionName",
            [UtilityBehavior.StructName] = "__StructName",
            [UtilityBehavior.ClassName] = "__ClassName",
            [UtilityBehavior.EnumName] = "__EnumName",
            [UtilityBehavior.TraitName] = "__TraitName",
            [UtilityBehavior.UsedTraitName] = "__UsedTraitName",
            [UtilityBehavior.InterfaceName] = "__InterfaceName",
            [UtilityBehavior.CompatibleTypeName] = "__CompatibleTypeName",
            [UtilityBehavior.PropertyName] = "__PropertyName",
            [UtilityBehavior.MethodName] = "__MethodName",
            [UtilityBehavior.ConstName] = "__ConstName",
            [UtilityBehavior.ObjectConstName] = "__ObjectConstName",
            [UtilityBehavior.EnumCaseName] = "__EnumCaseName",
        };

        public static bool IsSymbolNameBehavior(UtilityBehavior behavior) =>
            BehaviorNames.ContainsKey(behavior);

        public static bool TryGetBehavior(ICheckedType type, out UtilityBehavior behavior)
        {
            behavior = default;
            if (!TryGetUtilitySymbol(type, out var utility))
            {
                return false;
            }

            if (!IsSymbolNameBehavior(utility.Behavior))
            {
                return false;
            }

            behavior = utility.Behavior;
            return true;
        }

        public static bool IsSymbolNameType(ICheckedType type) =>
            TryGetBehavior(type, out _);

        public static bool IsTyhpInternal(ICheckedType type) =>
            TryGetBehavior(type, out var behavior) && behavior == UtilityBehavior.TyhpInternal;

        public static bool TryGetUtilitySymbol(ICheckedType type, out BuiltInUtilityTypeSymbol utility)
        {
            utility = null!;
            switch (type)
            {
                case SimpleCheckedType { ResolvedSymbol: BuiltInUtilityTypeSymbol simpleUtility }:
                    utility = simpleUtility;
                    return true;
                case GenericCheckedType { BaseType: SimpleCheckedType { ResolvedSymbol: BuiltInUtilityTypeSymbol genericUtility } }:
                    utility = genericUtility;
                    return true;
                default:
                    return false;
            }
        }

        public static IReadOnlyList<ICheckedType> GetTypeArguments(ICheckedType type) =>
            type is GenericCheckedType generic ? generic.TypeArguments : [];

        public static ICheckedType MakeSymbolNameType(
            UtilityBehavior behavior,
            GlobalScope globalScope,
            IReadOnlyList<ICheckedType>? typeArguments = null)
        {
            if (!BehaviorNames.TryGetValue(behavior, out var name))
            {
                return CheckedTypes.Unresolved;
            }

            var symbol = ((IBaseScope)globalScope).FindChildSymbolByName(name) as BuiltInUtilityTypeSymbol;
            if (symbol is null)
            {
                return CheckedTypes.Unresolved;
            }

            var baseType = CheckedTypes.FromSymbol(symbol);

            // Optional-single siblings: bare `__ClassName` ≡ `__ClassName<object>` (and enum /
            // interface / trait). Always materialize the default so arity-0 and arity-1-<object>
            // share one checked-type shape. Use the scope's `object` symbol so FQN identity
            // matches annotations that resolve `object` from global scope.
            if (IsOptionalSingleObjectBrand(behavior)
                && (typeArguments is null || typeArguments.Count == 0))
            {
                return new GenericCheckedType(baseType, [GetObjectTypeArg(globalScope)]);
            }

            if (typeArguments is { Count: > 0 })
            {
                return new GenericCheckedType(baseType, typeArguments);
            }

            return baseType;
        }

        /// <summary>
        /// <c>__ClassName</c> / <c>__EnumName</c> / <c>__InterfaceName</c> / <c>__TraitName</c>
        /// accept 0 or 1 type args; omitting means <c>object</c>.
        /// </summary>
        public static bool IsOptionalSingleObjectBrand(UtilityBehavior behavior) =>
            behavior is UtilityBehavior.ClassName
                or UtilityBehavior.EnumName
                or UtilityBehavior.InterfaceName
                or UtilityBehavior.TraitName;

        private static ICheckedType GetObjectTypeArg(GlobalScope globalScope)
        {
            var symbol = ((IBaseScope)globalScope).FindChildSymbolByName("object") as BuiltInTypeSymbol
                ?? new BuiltInTypeSymbol("object");
            return CheckedTypes.FromSymbol(symbol);
        }

        private static bool IsObjectTypeArg(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol { Name: var n } }
            && string.Equals(n, "object", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the immediate erasure target for a symbol-name type (not necessarily <c>string</c>).
        /// </summary>
        public static ICheckedType GetImmediateErasure(
            ICheckedType type,
            GlobalScope globalScope)
        {
            if (!TryGetBehavior(type, out var behavior))
            {
                return type;
            }

            return behavior switch
            {
                UtilityBehavior.TyhpInternal when GetTypeArguments(type) is { Count: > 0 } args => args[0],
                UtilityBehavior.TypedVarName => MakeSymbolNameType(UtilityBehavior.VarName, globalScope),
                UtilityBehavior.EnumName => MakeSymbolNameType(
                    UtilityBehavior.ClassName, globalScope, GetTypeArguments(type)),
                UtilityBehavior.MethodName => MakeSymbolNameType(UtilityBehavior.FunctionName, globalScope),
                UtilityBehavior.ObjectConstName => MakeSymbolNameType(UtilityBehavior.ConstName, globalScope),
                UtilityBehavior.EnumCaseName => MakeSymbolNameType(UtilityBehavior.ObjectConstName, globalScope,
                    GetTypeArguments(type)),
                UtilityBehavior.UsedTraitName => MakeSymbolNameType(UtilityBehavior.TraitName, globalScope),
                // Parametric forms erase to the default brand first, then to string:
                // `__ClassName<User>` → `__ClassName<object>` → `string`.
                // `__ClassName<object>` (and normalized bare) erase directly to string.
                UtilityBehavior.ClassName or UtilityBehavior.InterfaceName or UtilityBehavior.TraitName
                    when GetTypeArguments(type) is { Count: > 0 } args
                        && !(args.Count == 1 && IsObjectTypeArg(args[0]))
                    => MakeSymbolNameType(behavior, globalScope),
                UtilityBehavior.ClassName or UtilityBehavior.InterfaceName or UtilityBehavior.TraitName
                    => CheckedTypes.String,
                UtilityBehavior.CompatibleTypeName => CheckedTypes.UnionTypes(
                [
                    MakeSymbolNameType(UtilityBehavior.InterfaceName, globalScope),
                    MakeSymbolNameType(UtilityBehavior.ClassName, globalScope),
                    MakeSymbolNameType(UtilityBehavior.EnumName, globalScope),
                ]),
                _ => CheckedTypes.String,
            };
        }

        /// <summary>
        /// Walks the erasure chain until a non-symbol-name type is reached (typically <c>string</c>).
        /// </summary>
        public static ICheckedType GetFullErasure(ICheckedType type, GlobalScope globalScope)
        {
            var current = type;
            var visited = new HashSet<ICheckedType>();
            while (IsSymbolNameType(current) && visited.Add(current))
            {
                current = GetImmediateErasure(current, globalScope);
            }

            return current;
        }

        public static bool IsErasureAssignable(ICheckedType source, ICheckedType target, GlobalScope globalScope)
        {
            if (!IsSymbolNameType(source))
            {
                return false;
            }

            // Use TypeComparer equality (FQN-normalized) so scope-registered builtins match
            // constructed / resolved forms of the same brand.
            if (TypeComparer.AreTypesEqual(source, target))
            {
                return true;
            }

            var immediate = GetImmediateErasure(source, globalScope);
            if (TypeComparer.AreTypesEqual(immediate, target))
            {
                return true;
            }

            if (IsSymbolNameType(immediate))
            {
                return IsErasureAssignable(immediate, target, globalScope);
            }

            return false;
        }

        /// <summary>
        /// Subclass-as-<c>class-string</c> widening for <c>__CompatibleTypeName&lt;T&gt;</c> only.
        /// <c>__ClassName&lt;T&gt;</c> stays invariant between distinct brands; callers that need
        /// "name of <c>T</c> or a descendant" must use <c>__CompatibleTypeName&lt;T&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Accepts <c>__ClassName&lt;S&gt;</c> / <c>__EnumName&lt;S&gt;</c> / <c>__InterfaceName&lt;S&gt;</c> /
        /// <c>__CompatibleTypeName&lt;S&gt;</c> when <c>S</c> is the same as or a subtype of the
        /// target brand argument <c>T</c>. Bare <c>object</c> source brands are too wide unless
        /// <c>T</c> is also <c>object</c> (handled by ordinary subtyping of the type args).
        /// </remarks>
        public static bool IsCompatibleBrandAssignable(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!TryGetBehavior(target, out var targetBehavior)
                || targetBehavior != UtilityBehavior.CompatibleTypeName)
            {
                return false;
            }

            var targetArgs = GetTypeArguments(target);
            if (targetArgs.Count == 0)
            {
                return false;
            }

            if (!TryGetBehavior(source, out var sourceBehavior)
                || sourceBehavior is not (
                    UtilityBehavior.ClassName
                    or UtilityBehavior.EnumName
                    or UtilityBehavior.InterfaceName
                    or UtilityBehavior.CompatibleTypeName))
            {
                return false;
            }

            var sourceArgs = GetTypeArguments(source);
            if (sourceArgs.Count == 0)
            {
                return false;
            }

            var sourceArg = sourceArgs[0];
            var targetArg = targetArgs[0];
            return TypeComparer.AreTypesEqual(sourceArg, targetArg)
                || TypeComparer.IsSubtypeOf(sourceArg, targetArg, symbolTree, globalScope);
        }

        public static bool TryGetStringLiteral(ICheckedType type, out string value)
        {
            if (type is LiteralCheckedType { Value: string s })
            {
                value = s;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static readonly HashSet<string> BoolReturningGuards = new(StringComparer.OrdinalIgnoreCase)
        {
            "function_exists",
            "class_exists",
            "interface_exists",
            "trait_exists",
            "enum_exists",
            "property_exists",
            "method_exists",
            "is_a",
            "is_subclass_of",
            "variable_exists",
            "isset",
            "is_string",
            "is_int",
            "is_float",
            "is_bool",
            "is_array",
            "is_null",
            "is_object",
            "is_callable",
            "is_numeric",
        };

        public static bool IsBoolReturningGuard(string fnName) =>
            !string.IsNullOrEmpty(fnName) && BoolReturningGuards.Contains(fnName);

        public static string GetSimpleFunctionName(string? rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return string.Empty;
            }

            var trimmed = rawName.TrimStart('\\');
            var separator = trimmed.LastIndexOf('\\');
            return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        }
    }
}
