using System.Collections.Generic;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Constraint applied to an individual generic type parameter on a built-in type.
    /// </summary>
    public enum BuiltInGenericParameterConstraint
    {
        None,
        KeyIntOrString,
        ClassInterfaceOrStruct,
        ClassOrStruct,
        StringLiteralUnion,
        UnionType,
        Callable,
        AnyType,
        ReturnTypeRestricted,
        EnumOnly,
        /// <summary>
        /// Satisfied by the <c>object</c> built-in, classes, interfaces, enums, and structs.
        /// </summary>
        Object,
    }

    /// <summary>
    /// Describes one generic type parameter slot on a built-in or utility type.
    /// </summary>
    public readonly record struct BuiltInGenericParameterSpec(
        string Name,
        BuiltInGenericParameterConstraint Constraint = BuiltInGenericParameterConstraint.None
    );

    /// <summary>
    /// Metadata describing valid generic argument shapes for built-in types.
    /// Consumed by the checker (Story 08) during type argument validation.
    /// </summary>
    public sealed class GenericParameterRequirements
    {
        public int MinArity { get; init; }

        public int MaxArity { get; init; }

        /// <summary>
        /// When true, a single type argument uses shorthand semantics (e.g. <c>array&lt;T&gt;</c> implies key <c>int|string</c>).
        /// </summary>
        public bool SupportsSingleArgumentShorthand { get; init; }

        /// <summary>
        /// When true, the last type argument is the return type (callable return-last convention).
        /// </summary>
        public bool UsesReturnLastConvention { get; init; }

        /// <summary>
        /// Named parameter specs for fixed-arity forms. May be shorter than MaxArity when variadic prefixes are allowed.
        /// </summary>
        public IReadOnlyList<BuiltInGenericParameterSpec>? Parameters { get; init; }

        public static GenericParameterRequirements ArrayLike() => new()
        {
            MinArity = 1,
            MaxArity = 2,
            SupportsSingleArgumentShorthand = true,
            Parameters = new[]
            {
                new BuiltInGenericParameterSpec("TKey", BuiltInGenericParameterConstraint.KeyIntOrString),
                new BuiltInGenericParameterSpec("TValue", BuiltInGenericParameterConstraint.None),
            },
        };

        public static GenericParameterRequirements Callable() => new()
        {
            MinArity = 1,
            MaxArity = int.MaxValue,
            UsesReturnLastConvention = true,
            Parameters = new[]
            {
                new BuiltInGenericParameterSpec("TReturn", BuiltInGenericParameterConstraint.ReturnTypeRestricted),
            },
        };

        public static GenericParameterRequirements ZeroArity() => new()
        {
            MinArity = 0,
            MaxArity = 0,
        };

        /// <summary>
        /// Zero or one type argument. Omitting the argument means the default (documented on the type;
        /// e.g. <c>__ClassName</c> ≡ <c>__ClassName&lt;object&gt;</c>).
        /// </summary>
        public static GenericParameterRequirements OptionalSingle(
            string name,
            BuiltInGenericParameterConstraint constraint = BuiltInGenericParameterConstraint.None
        ) => new()
        {
            MinArity = 0,
            MaxArity = 1,
            Parameters = new[] { new BuiltInGenericParameterSpec(name, constraint) },
        };

        public static GenericParameterRequirements Single(string name, BuiltInGenericParameterConstraint constraint = BuiltInGenericParameterConstraint.None) => new()
        {
            MinArity = 1,
            MaxArity = 1,
            Parameters = new[] { new BuiltInGenericParameterSpec(name, constraint) },
        };

        public static GenericParameterRequirements Pair(
            string firstName,
            BuiltInGenericParameterConstraint firstConstraint,
            string secondName,
            BuiltInGenericParameterConstraint secondConstraint
        ) => new()
        {
            MinArity = 2,
            MaxArity = 2,
            Parameters = new[]
            {
                new BuiltInGenericParameterSpec(firstName, firstConstraint),
                new BuiltInGenericParameterSpec(secondName, secondConstraint),
            },
        };
    }
}
