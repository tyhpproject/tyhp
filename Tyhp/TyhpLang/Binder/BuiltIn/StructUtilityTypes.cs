using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers Story 08.5 Phase 5 struct/type utilities and Story 16.5 callable-signature
    /// utilities in global scope.
    /// </summary>
    public static class StructUtilityTypes
    {
        public static void PopulateGlobal(GlobalScope globalScope)
        {
            Register(globalScope, "__StructKey", UtilityBehavior.StructKey,
                GenericParameterRequirements.Single("TStructType", BuiltInGenericParameterConstraint.ClassOrStruct));

            Register(globalScope, "__StructRecord", UtilityBehavior.StructRecord,
                new GenericParameterRequirements
                {
                    MinArity = 3,
                    MaxArity = 3,
                    Parameters =
                    [
                        new BuiltInGenericParameterSpec("TStructType", BuiltInGenericParameterConstraint.ClassOrStruct),
                        new BuiltInGenericParameterSpec("TKeys", BuiltInGenericParameterConstraint.StringLiteralUnion),
                        new BuiltInGenericParameterSpec("TValueType", BuiltInGenericParameterConstraint.AnyType),
                    ],
                });

            Register(globalScope, "__StructDef", UtilityBehavior.StructDef,
                GenericParameterRequirements.Single("TRecordSet", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__StructPartial", UtilityBehavior.StructPartial,
                new GenericParameterRequirements
                {
                    MinArity = 3,
                    MaxArity = 3,
                    Parameters =
                    [
                        new BuiltInGenericParameterSpec("TStructType", BuiltInGenericParameterConstraint.ClassOrStruct),
                        new BuiltInGenericParameterSpec("TIncludeKeys", BuiltInGenericParameterConstraint.StringLiteralUnion),
                        new BuiltInGenericParameterSpec("TExcludeKeys", BuiltInGenericParameterConstraint.StringLiteralUnion),
                    ],
                });

            Register(globalScope, "__Properties", UtilityBehavior.Properties,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));

            Register(globalScope, "__FunctionReturnType", UtilityBehavior.FunctionReturnType,
                GenericParameterRequirements.Single("TFunctionName", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__MethodReturnType", UtilityBehavior.MethodReturnType,
                GenericParameterRequirements.Pair(
                    "TType", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct,
                    "TMethodName", BuiltInGenericParameterConstraint.StringLiteralUnion));

            Register(globalScope, "__CallableReturnType", UtilityBehavior.CallableReturnType,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
            Register(globalScope, "__CallableParametersStruct", UtilityBehavior.CallableParametersStruct,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
            Register(globalScope, "__CallableParametersTuple", UtilityBehavior.CallableParametersTuple,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
            Register(globalScope, "__CallableParametersRest", UtilityBehavior.CallableParametersRest,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));

            Register(globalScope, "__TypeDiff", UtilityBehavior.TypeDiff,
                GenericParameterRequirements.Pair(
                    "TType", BuiltInGenericParameterConstraint.AnyType,
                    "TExcludeType", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__AsNotNullable", UtilityBehavior.AsNotNullable,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__AsNullable", UtilityBehavior.AsNullable,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__AsReadOnly", UtilityBehavior.AsReadOnly,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));
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
