using System.Globalization;
using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpScalarAst : Base2Ast, IExpression, IScalar
    {
        /*
        Value Types:
            string
            int
            float
            true
            false
            null

        Types:
            null
            callable
            array
            bool
            float
            int
            void
            mixed
            iterable
            object
            resource
            string
            struct
        */

        
        private const short SCALAR_TYPE_OFFSET = 8000;

        public PhpScalarType ScalarType => GetEnumFlags<PhpScalarType>(SCALAR_TYPE_OFFSET).FirstOrDefault();
        
        public static PhpScalarAst Create(TokenValueAst tokenValue, PhpScalarType scalarType, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpScalarAst {
                Children = [tokenValue],
            };
            
            result.SetFlag(SCALAR_TYPE_OFFSET, scalarType);
            result.SetContext(context, languageMode);

            // PHP/Tyhp numeric literals allow `_` as a digit separator (e.g. `1_000_000`),
            // which none of the parse routines below understand directly.
            var tokenString = tokenValue.ValueString?.Replace("_", string.Empty);

            switch (scalarType) {
                case PhpScalarType.Integer:
                    result.ValueInt64 = long.TryParse(
                        tokenString ?? "0",
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var intValue) ? intValue : null;
                    break;
                    
                case PhpScalarType.Float:
                    result.ValueDecimal = decimal.TryParse(
                        tokenString ?? "0",
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var floatValue) ? floatValue : null;
                    break;
                    
                case PhpScalarType.OctalNumber:
                    try {
                        // T_ONUMBER always carries an explicit '0o'/'0O' prefix (see PhpLexer.g4's
                        // ONUM fragment) — legacy leading-zero octal (`017`) lexes as T_LNUMBER/
                        // Integer instead and never reaches this branch.
                        var octalString = tokenString?.StartsWith("0o", StringComparison.OrdinalIgnoreCase) == true
                            ? tokenString[2..]
                            : tokenString;
                        result.ValueInt64 = Convert.ToInt64(octalString ?? "0", 8);
                    } catch {
                        result.ValueInt64 = null;
                    }
                    break;
                    
                case PhpScalarType.HexNumber:
                    try {
                        // Remove '0x' or '0X' prefix if present
                        var hexString = tokenString?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true ? tokenString[2..] : tokenString;
                        result.ValueInt64 = Convert.ToInt64(hexString ?? "0", 16);
                    } catch {
                        result.ValueInt64 = null;
                    }
                    break;
                    
                case PhpScalarType.BinaryNumber:
                    try {
                        // Remove '0b' or '0B' prefix if present
                        var binaryString = tokenString?.StartsWith("0b", StringComparison.OrdinalIgnoreCase) == true ? tokenString[2..] : tokenString;
                        result.ValueInt64 = Convert.ToInt64(binaryString ?? "0", 2);
                    } catch {
                        result.ValueInt64 = null;
                    }
                    break;
                    
                case PhpScalarType.String:
                    result.ValueString = tokenValue.ValueString ?? string.Empty;
                    break;
                    
                default:
                    throw new Exception($"Unexpected scalar type: {scalarType}"); // TODO: do proper parser error
            }
            
            return result;
        }

        /// <summary>Creates an integer literal AST node for emitter synthesis (no parse context).</summary>
        internal static PhpScalarAst CreateIntegerFromContext(Base2Ast context, long value)
        {
            var token = TokenValueAst.CreateFromContext(value.ToString(), TyhpLang.Parser.TyhpParser.T_LNUMBER, context);

            var result = new PhpScalarAst
            {
                Children = [token],
                ValueInt64 = value,
            };
            result.SetFlag(SCALAR_TYPE_OFFSET, PhpScalarType.Integer);
            result.SetContext(context);
            return result;
        }

        /// <summary>Creates a string literal AST node for emitter synthesis (no parse context).</summary>
        internal static PhpScalarAst CreateStringFromContext(Base2Ast context, string value)
        {
            var token = TokenValueAst.CreateFromContext(
                $"'{value.Replace("'", "\\'")}'",
                TyhpLang.Parser.TyhpParser.T_CONSTANT_ENCAPSED_STRING,
                context);

            var result = new PhpScalarAst
            {
                Children = [token],
                ValueString = value,
            };
            result.SetFlag(SCALAR_TYPE_OFFSET, PhpScalarType.String);
            result.SetContext(context);
            return result;
        }
    }
} 