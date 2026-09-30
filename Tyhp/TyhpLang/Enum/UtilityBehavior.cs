namespace Tyhp.TyhpLang.Enum
{
    /// <summary>
    /// Describes the checker transformation performed by a built-in global <c>__</c> utility type.
    /// Enum member names may differ from the user-facing spelling (e.g. <c>AsNullable</c> is
    /// registered as <c>__Nullable</c>); dispatch is by this value, not the symbol name.
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
        CallableParametersSlice,
        TypeDiff,
        AsNotNullable,
        AsNullable,
        AsReadOnly,

        /// <summary>
        /// Constructable object-shape constraint (<c>__New&lt;Shape&gt;</c>). Values are
        /// instances of the shape whose class is <c>new</c>-able as the shape constructor.
        /// </summary>
        New,

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

        // Closure / indexing utilities (__SuperType, __CurrentScope, __IndexKeys, …).
        SuperType,
        SuperTypeName,
        CurrentScope,
        CallableThis,
        CallableScope,
        IndexKeys,
        IndexValueType,
        IndexValueTypes,
    }
}
