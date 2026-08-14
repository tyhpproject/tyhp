namespace Tyhp.TyhpLang.Enum
{
    /// <summary>
    /// Describes the checker transformation performed by a built-in utility type in the <c>\Tyhp</c> namespace.
    /// Values match the utility type name for dispatch in Story 08's <c>UtilityTypeResolver</c>.
    /// </summary>
    public enum UtilityBehavior
    {
        Readonly,
        Partial,
        Required,
        Pick,
        Omit,
        Record,
        Exclude,
        Extract,
        NonNullable,
        Nullable,
        ReturnType,
        Parameters,
        Awaited,

        // Symbol-name types (Story 08.5) — checker-only brands that erase to string.
        TyhpInternal,
        VarName,
        TypedVarName,
        FunctionName,
        StructName,
        ClassName,
        EnumName,
        TraitName,
        UsedTraitName,
        InterfaceName,
        CompatibleTypeName,
        PropertyName,
        MethodName,
        ConstName,
        ObjectConstName,
        EnumCaseName,

        // Struct/type utilities (Story 08.5 Phase 5).
        StructKey,
        StructRecord,
        StructDef,
        StructPartial,
        Properties,
        FunctionReturnType,
        MethodReturnType,
        // Callable-keyed signature utilities (Story 16.5 Phase 1).
        CallableReturnType,
        CallableParametersStruct,
        CallableParametersTuple,
        CallableParametersRest,
        TypeDiff,
        AsNotNullable,
        AsNullable,
        AsReadOnly,

        // Type-name string algebra (Story 08.5 Phase 7).
        BaseTypeName,
        NullableBaseTypeName,
        BaseUnionTypeName,
        UnionTypeName,
        BaseIntersectTypeName,
        IntersectTypeName,
        NotNullableUnionTypeName,
        NotNullableIntersectTypeName,
        NotNullableTypeName,
        TypeName,
        NonMatchingStringType,
        AsNotNullableTypeName,
        AsNullableTypeName,
        AsTypeName,
        AsType,
    }
}
