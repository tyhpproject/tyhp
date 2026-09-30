using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers built-in magic utility types in global scope:
    /// <c>__SuperType</c>, <c>__SuperTypeName</c>, <c>__CurrentScope</c>,
    /// <c>__CallableThis</c>, <c>__CallableScope</c>, <c>__IndexKeys</c>,
    /// <c>__IndexValueType</c>, and <c>__IndexValueTypes</c>.
    /// Overlay aliases <c>__ClosureThis</c> / <c>__ClosureScope</c> are not registered here.
    /// </summary>
    public static class MagicUtilityTypes
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            Register(globalScope, "__SuperType", UtilityBehavior.SuperType,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__SuperTypeName", UtilityBehavior.SuperTypeName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__CurrentScope", UtilityBehavior.CurrentScope,
                GenericParameterRequirements.ZeroArity());

            Register(globalScope, "__CallableThis", UtilityBehavior.CallableThis,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
            Register(globalScope, "__CallableScope", UtilityBehavior.CallableScope,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));

            Register(globalScope, "__IndexKeys", UtilityBehavior.IndexKeys,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__IndexValueType", UtilityBehavior.IndexValueType,
                GenericParameterRequirements.Pair(
                    "T", BuiltInGenericParameterConstraint.AnyType,
                    "K", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__IndexValueTypes", UtilityBehavior.IndexValueTypes,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));
        }

        private static void Register(
            GlobalScope globalScope,
            string name,
            UtilityBehavior behavior,
            GenericParameterRequirements requirements)
        {
            globalScope.AddChildSymbol(new BuiltInUtilityTypeSymbol(name, behavior, requirements));
        }
    }
}
