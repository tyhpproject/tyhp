using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers built-in checker utilities in global scope as <c>__Name</c> symbols.
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
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.Object));

            Register(globalScope, "__New", UtilityBehavior.New,
                GenericParameterRequirements.Single("TShape", BuiltInGenericParameterConstraint.AnyType));

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
            Register(globalScope, "__CallableParametersSlice", UtilityBehavior.CallableParametersSlice,
                new GenericParameterRequirements
                {
                    MinArity = 1,
                    MaxArity = 3,
                    Parameters =
                    [
                        new BuiltInGenericParameterSpec("TCallable", BuiltInGenericParameterConstraint.Callable),
                        new BuiltInGenericParameterSpec("TStart", BuiltInGenericParameterConstraint.NonNegativeIntLiteral),
                        new BuiltInGenericParameterSpec("TMin", BuiltInGenericParameterConstraint.NonNegativeIntLiteral),
                    ],
                });

            Register(globalScope, "__TypeDiff", UtilityBehavior.TypeDiff,
                GenericParameterRequirements.Pair(
                    "TType", BuiltInGenericParameterConstraint.AnyType,
                    "TExcludeType", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__NonNullable", UtilityBehavior.AsNotNullable,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__Nullable", UtilityBehavior.AsNullable,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__AsReadOnly", UtilityBehavior.AsReadOnly,
                GenericParameterRequirements.Single("TType", BuiltInGenericParameterConstraint.AnyType));

            Register(globalScope, "__Partial", UtilityBehavior.Partial,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(globalScope, "__Required", UtilityBehavior.Required,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(globalScope, "__Pick", UtilityBehavior.Pick,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.ClassOrStruct, "K", BuiltInGenericParameterConstraint.StringLiteralUnion));
            Register(globalScope, "__Omit", UtilityBehavior.Omit,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.ClassOrStruct, "K", BuiltInGenericParameterConstraint.StringLiteralUnion));
            Register(globalScope, "__Record", UtilityBehavior.Record,
                GenericParameterRequirements.Pair("K", BuiltInGenericParameterConstraint.KeyIntOrString, "V", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__Exclude", UtilityBehavior.Exclude,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.UnionType, "U", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__Extract", UtilityBehavior.Extract,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.UnionType, "U", BuiltInGenericParameterConstraint.AnyType));
            Register(globalScope, "__Awaited", UtilityBehavior.Awaited,
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
