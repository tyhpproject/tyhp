using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers checker-only symbol-name types in global scope (Story 08.5).
    /// All erase to plain <c>string</c> in emitted PHP.
    /// </summary>
    public static class SymbolNameTypes
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            Register(globalScope, "__TyhpInternal", UtilityBehavior.TyhpInternal,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__VarName", UtilityBehavior.VarName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__TypedVarName", UtilityBehavior.TypedVarName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__FunctionName", UtilityBehavior.FunctionName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__StructName", UtilityBehavior.StructName,
                GenericParameterRequirements.ZeroArity());

            // Bare `__ClassName` ≡ `__ClassName<object>` (default type arg). Same pattern for enum /
            // interface / trait siblings. Parametric `__ClassName<T>` is invariant in T (exact class
            // name); use `__CompatibleTypeName<T>` for subclass-as-class-string widening. Both 0-
            // and 1-arity erase to plain string.
            Register(globalScope, "__ClassName", UtilityBehavior.ClassName,
                GenericParameterRequirements.OptionalSingle("TObject", BuiltInGenericParameterConstraint.Object));
            Register(globalScope, "__EnumName", UtilityBehavior.EnumName,
                GenericParameterRequirements.OptionalSingle("TObject", BuiltInGenericParameterConstraint.Object));
            Register(globalScope, "__TraitName", UtilityBehavior.TraitName,
                GenericParameterRequirements.OptionalSingle("TObject", BuiltInGenericParameterConstraint.Object));
            Register(globalScope, "__InterfaceName", UtilityBehavior.InterfaceName,
                GenericParameterRequirements.OptionalSingle("TObject", BuiltInGenericParameterConstraint.Object));

            Register(globalScope, "__UsedTraitName", UtilityBehavior.UsedTraitName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassOrStruct));
            // Covariant in T: accepts `__ClassName<S>` / sibling brands / `__CompatibleTypeName<S>`
            // when S is the same as or a subtype of T (subclass-as-class-string utility).
            Register(globalScope, "__CompatibleTypeName", UtilityBehavior.CompatibleTypeName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(globalScope, "__PropertyName", UtilityBehavior.PropertyName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(globalScope, "__MethodName", UtilityBehavior.MethodName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));

            Register(globalScope, "__ConstName", UtilityBehavior.ConstName,
                GenericParameterRequirements.ZeroArity());
            Register(globalScope, "__ObjectConstName", UtilityBehavior.ObjectConstName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassOrStruct));
            Register(globalScope, "__EnumCaseName", UtilityBehavior.EnumCaseName,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.EnumOnly));
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
