using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>Resolves interpolation-hole expressions in template-string types to checked types.</summary>
    internal static class TemplateStringHoleResolver
    {
        /// <summary>
        /// <paramref name="resolveName"/> resolves a type name written in a hole (<c>${string}</c>,
        /// <c>${ApiMethod}</c>, <c>${\App\ApiMethod}</c>, <c>${Wrap&lt;'a'&gt;}</c>).
        /// <paramref name="reportUnsupported"/> receives each hole (or union member) that is not a
        /// type name, a string or number literal, or a union of them; it resolves as unresolved.
        /// </summary>
        public static ICheckedType Resolve(
            IExpression holeExpr,
            CheckerState state,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            Func<PhpNameAst, ICheckedType> resolveName,
            Action<IExpression> reportUnsupported)
        {
            if (holeExpr is ITypeExpression typeExpr)
            {
                return resolveType(typeExpr, state, false, true);
            }

            if (holeExpr is PhpNameAst name)
            {
                return resolveName(name);
            }

            if (holeExpr is TokenValueAst token)
            {
                if (token.ValueString is { } text &&
                    text.Length >= 2 &&
                    ((text[0] == '\'' && text[^1] == '\'') || (text[0] == '"' && text[^1] == '"')))
                {
                    if (PhpStringLiteralHelper.TryDecodeQuotedTokenText(text, out var decoded))
                    {
                        return new LiteralCheckedType(
                            decoded,
                            new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("string")));
                    }
                }

                if (token.ValueInt64 is TyhpParser.T_STRING or TyhpParser.T_STRING_VARNAME
                    && token is Base2Ast tokenNode
                    && !string.IsNullOrEmpty(token.ValueString))
                {
                    return resolveName(PhpNameAst.CreateFromContext(token.ValueString, tokenNode));
                }
            }

            if (holeExpr is PhpBinaryOpAst binary && binary.Operator?.ValueInt64 == TyhpParser.T_SYM_PIPE)
            {
                var left = Resolve(binary.Left!, state, resolveType, resolveName, reportUnsupported);
                var right = Resolve(binary.Right!, state, resolveType, resolveName, reportUnsupported);
                return CheckedTypes.UnionTypes([left, right]);
            }

            if (holeExpr is PhpEncapsListAst encaps &&
                PhpStringLiteralHelper.TryGetStaticLiteral(encaps, out var literal))
            {
                return new LiteralCheckedType(
                    literal,
                    new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("string")));
            }

            if (TryResolveNumericLiteral(holeExpr, out var numeric))
            {
                return numeric;
            }

            reportUnsupported(holeExpr);
            return CheckedTypes.Unresolved;
        }

        /// <summary><c>${1|2}</c>, <c>${-3}</c>, <c>${2.5}</c>: int and float literal hole types.</summary>
        private static bool TryResolveNumericLiteral(IExpression holeExpr, out ICheckedType type)
        {
            var negate = false;
            if (holeExpr is PhpUnaryOpAst { Operand: PhpScalarAst operand } unary
                && unary.Operator?.ValueString is "-" or "+")
            {
                negate = unary.Operator.ValueString == "-";
                holeExpr = operand;
            }

            switch (holeExpr)
            {
                case PhpScalarAst { ScalarType: PhpScalarType.Float, ValueDecimal: decimal value }:
                    type = new LiteralCheckedType(
                        negate ? -value : value,
                        new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("float")));
                    return true;
                case PhpScalarAst
                {
                    ScalarType: PhpScalarType.Integer or PhpScalarType.OctalNumber
                        or PhpScalarType.HexNumber or PhpScalarType.BinaryNumber,
                    ValueInt64: long number,
                }:
                    type = new LiteralCheckedType(
                        negate ? -number : number,
                        new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("int")));
                    return true;
                default:
                    type = CheckedTypes.Unresolved;
                    return false;
            }
        }
    }
}
