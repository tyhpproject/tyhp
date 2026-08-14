using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private static ICheckedType InferBinaryOperator(
            PhpBinaryOperator op,
            ICheckedType left,
            ICheckedType right)
        {
            return op switch
            {
                PhpBinaryOperator.Plus => InferAddition(left, right),
                PhpBinaryOperator.Minus or PhpBinaryOperator.Multiply => InferNumericPromotion(left, right),
                PhpBinaryOperator.Divide => InferDivision(left, right),
                PhpBinaryOperator.Modulo => CheckedTypes.Int,
                PhpBinaryOperator.Power => InferExponentiation(left, right),
                PhpBinaryOperator.Concat => CheckedTypes.String,
                PhpBinaryOperator.BitwiseAnd or PhpBinaryOperator.BitwiseOr or PhpBinaryOperator.BitwiseXor
                    or PhpBinaryOperator.ShiftLeft or PhpBinaryOperator.ShiftRight => CheckedTypes.Int,
                PhpBinaryOperator.Equal or PhpBinaryOperator.NotEqual or PhpBinaryOperator.Identical
                    or PhpBinaryOperator.NotIdentical or PhpBinaryOperator.LessThan or PhpBinaryOperator.LessThanOrEqual
                    or PhpBinaryOperator.GreaterThan or PhpBinaryOperator.GreaterThanOrEqual
                    or PhpBinaryOperator.LogicalAnd or PhpBinaryOperator.LogicalOr or PhpBinaryOperator.LogicalXor
                    or PhpBinaryOperator.BooleanAnd or PhpBinaryOperator.BooleanOr
                    => CheckedTypes.Bool,
                PhpBinaryOperator.Spaceship => CheckedTypes.Int,
                // Pipe result typing lives in InferBinary (RHS callable return); do not promote here.
                PhpBinaryOperator.Pipe => CheckedTypes.Unresolved,
                _ => CheckedTypes.Unresolved,
            };
        }

        private static ICheckedType InferAddition(ICheckedType left, ICheckedType right)
        {
            if (IsDecimalType(left) || IsDecimalType(right))
            {
                return CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("decimal"));
            }

            return InferNumericPromotion(left, right);
        }

        private static ICheckedType InferDivision(ICheckedType left, ICheckedType right)
        {
            if (IsFloatType(left) || IsFloatType(right))
            {
                return CheckedTypes.Float;
            }

            if (IsIntType(left) && IsIntType(right))
            {
                return CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.Float);
            }

            return InferNumericPromotion(left, right);
        }

        private static ICheckedType InferExponentiation(ICheckedType left, ICheckedType right)
        {
            if (IsFloatType(left) || IsFloatType(right))
            {
                return CheckedTypes.Float;
            }

            if (IsIntType(left) && IsIntType(right))
            {
                return CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.Float);
            }

            return InferNumericPromotion(left, right);
        }

        private static ICheckedType InferNumericPromotion(ICheckedType left, ICheckedType right)
        {
            if (IsFloatType(left) || IsFloatType(right))
            {
                return CheckedTypes.Float;
            }

            if (IsIntType(left) && IsIntType(right))
            {
                return CheckedTypes.Int;
            }

            if (IsDecimalType(left) || IsDecimalType(right))
            {
                return CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("decimal"));
            }

            return CheckedTypes.Unresolved;
        }

        private static bool IsNumericType(ICheckedType type) =>
            IsIntType(type) || IsFloatType(type) || IsDecimalType(type);

        private static bool IsIntType(ICheckedType type) =>
            type.DisplayName == "int" ||
            (type is LiteralCheckedType literal &&
             literal.UnderlyingType.DisplayName == "int" &&
             literal.Value is int or long);

        private static bool IsFloatType(ICheckedType type) =>
            type.DisplayName == "float" ||
            (type is LiteralCheckedType literal &&
             literal.UnderlyingType.DisplayName == "float" &&
             literal.Value is float or double or decimal);

        private static bool IsDecimalType(ICheckedType type) =>
            type.DisplayName == "decimal";
    }
}
