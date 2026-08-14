using Tyhp.TyhpLang.Emitter.NameGeneration;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Generates the emitted PHP method names for operator overloads (Story 11 §8 redesign).
    ///
    /// Every operator maps to a single, deterministic static method name (no type suffixes and no
    /// <c>_N</c> collision numbers): multiple forms of the same operator collapse into one method
    /// that dispatches on operand types. The only instance method is <c>convert</c>'s to-form
    /// (<c>__to{T}()</c>), which must satisfy PHP magic/<see cref="System.Stringable"/> and the
    /// <c>\Tyhp\Contracts\*Convertible</c> instance interfaces.
    /// </summary>
    internal static class OperatorMethodNameGenerator
    {
        /// <summary>The single static method name for a binary/unary/"word" operator.</summary>
        public static string GetMethodName(OverloadableOperator op) => op switch
        {
            // Binary
            OverloadableOperator.Add => "__add",
            OverloadableOperator.Subtract => "__subtract",
            OverloadableOperator.Multiply => "__multiply",
            OverloadableOperator.Divide => "__divide",
            OverloadableOperator.Mod => "__mod",
            OverloadableOperator.Pow => "__pow",
            OverloadableOperator.BitwiseAnd => "__bwAnd",
            OverloadableOperator.BitwiseOr => "__bwOr",
            OverloadableOperator.BitwiseXor => "__bwXor",
            OverloadableOperator.BitwiseShiftLeft => "__bwSL",
            OverloadableOperator.BitwiseShiftRight => "__bwSR",
            OverloadableOperator.Concat => "__concat",
            OverloadableOperator.CompareLessThan => "__isLessThan",
            OverloadableOperator.CompareLessThanOrEqualTo => "__isLessThanOrEqual",
            OverloadableOperator.CompareGreaterThan => "__isGreaterThan",
            OverloadableOperator.CompareGreaterThanOrEqualTo => "__isGreaterThanOrEqual",
            OverloadableOperator.CompareEqual => "__isEqual",
            OverloadableOperator.CompareNotEqual => "__isNotEqual",
            OverloadableOperator.CompareIdentical => "__isExact",
            OverloadableOperator.CompareNotIdentical => "__isNotExact",
            OverloadableOperator.CompareSpaceship => "__compare",

            // Unary
            OverloadableOperator.Plus => "__asNumeric",
            OverloadableOperator.Minus => "__negate",
            OverloadableOperator.Increment => "__increment",
            OverloadableOperator.Decrement => "__decrement",
            OverloadableOperator.BitwiseNot => "__bwNot",
            OverloadableOperator.BooleanNot => "__not",

            // Word
            OverloadableOperator.IsEmpty => "__isEmpty",

            _ => "",
        };

        /// <summary>The static factory method name for a <c>convert</c>-from overload.</summary>
        public const string ConvertFromMethodName = "__from";

        /// <summary>
        /// The instance method name for a <c>convert</c>-to overload targeting <paramref name="targetTypeName"/>.
        /// Scalar and decimal targets use the fixed magic/contract names (<c>__toString</c>,
        /// <c>__toBool</c>, <c>__toInt</c>, <c>__toFloat</c>, <c>__toDecimal</c>); any other type uses
        /// <c>__to{FormattedSegment}</c>.
        /// </summary>
        public static string GetConvertToMethodName(string? targetTypeName)
        {
            switch (NormalizeTargetName(targetTypeName))
            {
                case "string": return "__toString";
                case "bool": return "__toBool";
                case "int": return "__toInt";
                case "float": return "__toFloat";
                case "decimal": return "__toDecimal";
            }

            var segment = TypeNameFormatter.FormatTypeNameSegment(
                (targetTypeName ?? "").TrimStart('\\').Split('\\')[^1]);
            return string.IsNullOrEmpty(segment) ? "__toMixed" : "__to" + segment;
        }

        /// <summary>
        /// The <c>\Tyhp\Contracts\*Convertible</c> interface FQN a convert-to target requires. Scalar
        /// and decimal targets map to their specific contract; all other targets use the base
        /// <c>\Tyhp\Contracts\Convertible</c> marker.
        /// </summary>
        public static string GetConvertibleInterface(string? targetTypeName) => NormalizeTargetName(targetTypeName) switch
        {
            "string" => "\\Tyhp\\Contracts\\StringConvertible",
            "bool" => "\\Tyhp\\Contracts\\BoolConvertible",
            "int" => "\\Tyhp\\Contracts\\IntConvertible",
            "float" => "\\Tyhp\\Contracts\\FloatConvertible",
            "decimal" => "\\Tyhp\\Contracts\\DecimalConvertible",
            _ => "\\Tyhp\\Contracts\\Convertible",
        };

        /// <summary>True when the convert-to target is <c>decimal</c> (needs the decimal package).</summary>
        public static bool IsDecimalConvertTarget(string? targetTypeName)
            => NormalizeTargetName(targetTypeName) == "decimal";

        private static string NormalizeTargetName(string? targetTypeName)
        {
            var name = (targetTypeName ?? "").Trim();
            if (name.Length == 0)
            {
                return "";
            }

            var simple = name.TrimStart('\\').Split('\\')[^1];
            return simple switch
            {
                "string" or "String" => "string",
                "bool" or "Bool" or "boolean" => "bool",
                "int" or "Int" or "integer" => "int",
                "float" or "Float" or "double" => "float",
                "decimal" or "Decimal" => "decimal",
                _ => simple,
            };
        }
    }
}
