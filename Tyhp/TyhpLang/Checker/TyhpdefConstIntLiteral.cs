using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 21.8 Decision 18: a tyhpdef <c>const int NAME ?? N</c> with a known integer
    /// start value is the literal <c>N</c> at overload selection (and at use-site inference),
    /// not the general <c>int</c> catch-all. No known start value keeps the declared type.
    /// </summary>
    internal static class TyhpdefConstIntLiteral
    {
        public static bool TryGetLiteralType(IBaseSymbol? symbol, out ICheckedType literal)
        {
            literal = CheckedTypes.Unresolved;
            if (!TryGetValue(symbol, out var value))
            {
                return false;
            }

            literal = new LiteralCheckedType(
                value,
                new SimpleCheckedType(new BuiltInTypeSymbol("int")));
            return true;
        }

        public static bool TryGetValue(IBaseSymbol? symbol, out long value)
        {
            value = 0;
            var expr = symbol switch
            {
                ConstantSymbol constant =>
                    constant.ValueExpression ?? StartValueFromDeclaringNode(constant.DeclaringAstNode),
                ObjectConstantSymbol objectConstant =>
                    objectConstant.ValueExpression ?? StartValueFromDeclaringNode(objectConstant.DeclaringAstNode),
                _ => null,
            };

            return TryReadInteger(expr, out value);
        }

        public static bool TryGetIntegerLiteralValue(ICheckedType type, out long value)
        {
            switch (type)
            {
                case LiteralCheckedType { Value: int i }:
                    value = i;
                    return true;
                case LiteralCheckedType { Value: long l }:
                    value = l;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        public static bool TryGetIntegerFromExpression(IExpression? expr, out long value) =>
            TryReadInteger(expr, out value);

        private static IExpression? StartValueFromDeclaringNode(IBase2Ast? node) =>
            node switch
            {
                TyhpdefImportConstAst import => import.CoalesceExpr,
                TyhpdefImportConstDeclAst importMember => importMember.CoalesceExpr,
                TyhpdefConstDeclAst classConst => classConst.CoalesceExpr,
                PhpConstDeclAst phpConst => phpConst.Value,
                _ => null,
            };

        private static bool TryReadInteger(IExpression? expr, out long value)
        {
            value = 0;
            switch (expr)
            {
                case PhpScalarAst scalar
                    when scalar.ScalarType is PhpScalarType.Integer
                        or PhpScalarType.OctalNumber
                        or PhpScalarType.HexNumber
                        or PhpScalarType.BinaryNumber
                        && scalar.ValueInt64 is long scalarValue:
                    value = scalarValue;
                    return true;
                case PhpUnaryOpAst unary when unary.Operand is not null:
                {
                    var op = unary.Operator?.ValueString;
                    if (op is "-" or "+" && TryReadInteger(unary.Operand, out var inner))
                    {
                        value = op == "-" ? -inner : inner;
                        return true;
                    }

                    return false;
                }
                default:
                    return false;
            }
        }
    }
}
