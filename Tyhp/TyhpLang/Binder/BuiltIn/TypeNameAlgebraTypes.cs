using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers Story 08.5 Phase 7 type-name string algebra types in global scope.
    /// </summary>
    public static class TypeNameAlgebraTypes
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            Register(globalScope, "__BaseTypeName", UtilityBehavior.BaseTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__NullableBaseTypeName", UtilityBehavior.NullableBaseTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__BaseUnionTypeName", UtilityBehavior.BaseUnionTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__UnionTypeName", UtilityBehavior.UnionTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__BaseIntersectTypeName", UtilityBehavior.BaseIntersectTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__IntersectTypeName", UtilityBehavior.IntersectTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__NotNullableUnionTypeName", UtilityBehavior.NotNullableUnionTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__NotNullableIntersectTypeName", UtilityBehavior.NotNullableIntersectTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__NotNullableTypeName", UtilityBehavior.NotNullableTypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__TypeName", UtilityBehavior.TypeName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__NonMatchingStringType", UtilityBehavior.NonMatchingStringType,
                GenericParameterRequirements.ZeroArity());

            Register(globalScope, "__AsNotNullableTypeName", UtilityBehavior.AsNotNullableTypeName,
                GenericParameterRequirements.Single("TTypeName", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__AsNullableTypeName", UtilityBehavior.AsNullableTypeName,
                GenericParameterRequirements.Single("TTypeName", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__AsTypeName", UtilityBehavior.AsTypeName,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__AsType", UtilityBehavior.AsType,
                GenericParameterRequirements.Single("TTypeName", BuiltInGenericParameterConstraint.AnyType));
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
