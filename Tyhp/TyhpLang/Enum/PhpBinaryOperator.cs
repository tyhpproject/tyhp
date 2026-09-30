using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpBinaryOperator
    {
        // Arithmetic
        Plus,
        Minus,
        Multiply,
        Divide,
        Modulo,
        Power,
        
        // Comparison
        Equal,
        NotEqual,
        Identical,
        NotIdentical,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Spaceship,
        
        // Logical
        LogicalAnd,
        LogicalOr,
        LogicalXor,
        BooleanAnd,
        BooleanOr,
        
        // Bitwise
        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
        ShiftLeft,
        ShiftRight,
        
        // String
        Concat,
        
        // Null coalescing
        Coalesce,

        // PHP 8.5 pipe `|>`
        Pipe,
        
        // instanceof
        InstanceOf
    }
    
    public static class PhpBinaryOperatorExtensions
    {
        public static PhpBinaryOperator? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1, token?.Text);

        /// <summary>
        /// When <paramref name="text"/> is present it is authoritative. Tyhpdef token ids are not
        /// <c>TyhpParser</c> ids; the integer is used only when the spelling is absent.
        /// </summary>
        public static PhpBinaryOperator? FromToken(int token, string? text = null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                var spelling = text.Trim();
                if (spelling.Length > 0 && char.IsLetter(spelling[0]))
                {
                    return spelling.ToLowerInvariant() switch
                    {
                        "and" => PhpBinaryOperator.LogicalAnd,
                        "or" => PhpBinaryOperator.LogicalOr,
                        "xor" => PhpBinaryOperator.LogicalXor,
                        "instanceof" or "is" => PhpBinaryOperator.InstanceOf,
                        _ => null,
                    };
                }

                return spelling switch
                {
                    "+" => PhpBinaryOperator.Plus,
                    "-" => PhpBinaryOperator.Minus,
                    "*" => PhpBinaryOperator.Multiply,
                    "/" => PhpBinaryOperator.Divide,
                    "%" => PhpBinaryOperator.Modulo,
                    "**" => PhpBinaryOperator.Power,
                    "==" => PhpBinaryOperator.Equal,
                    "!=" => PhpBinaryOperator.NotEqual,
                    "===" => PhpBinaryOperator.Identical,
                    "!==" => PhpBinaryOperator.NotIdentical,
                    ">" => PhpBinaryOperator.GreaterThan,
                    "<" => PhpBinaryOperator.LessThan,
                    "<=" => PhpBinaryOperator.LessThanOrEqual,
                    ">=" => PhpBinaryOperator.GreaterThanOrEqual,
                    "<=>" => PhpBinaryOperator.Spaceship,
                    "&&" => PhpBinaryOperator.BooleanAnd,
                    "||" => PhpBinaryOperator.BooleanOr,
                    "&" => PhpBinaryOperator.BitwiseAnd,
                    "|" => PhpBinaryOperator.BitwiseOr,
                    "^" => PhpBinaryOperator.BitwiseXor,
                    "<<" => PhpBinaryOperator.ShiftLeft,
                    ">>" => PhpBinaryOperator.ShiftRight,
                    "." => PhpBinaryOperator.Concat,
                    "??" => PhpBinaryOperator.Coalesce,
                    "|>" => PhpBinaryOperator.Pipe,
                    _ => null,
                };
            }

            return token switch
            {
                TyhpLang.Parser.TyhpParser.T_SYM_PLUS => PhpBinaryOperator.Plus,
                TyhpLang.Parser.TyhpParser.T_SYM_MINUS => PhpBinaryOperator.Minus,
                TyhpLang.Parser.TyhpParser.T_SYM_ASTERISK => PhpBinaryOperator.Multiply,
                TyhpLang.Parser.TyhpParser.T_SYM_SLASH => PhpBinaryOperator.Divide,
                TyhpLang.Parser.TyhpParser.T_SYM_PERCENT => PhpBinaryOperator.Modulo,
                TyhpLang.Parser.TyhpParser.T_POW => PhpBinaryOperator.Power,
                TyhpLang.Parser.TyhpParser.T_IS_EQUAL => PhpBinaryOperator.Equal,
                TyhpLang.Parser.TyhpParser.T_IS_NOT_EQUAL => PhpBinaryOperator.NotEqual,
                TyhpLang.Parser.TyhpParser.T_IS_IDENTICAL => PhpBinaryOperator.Identical,
                TyhpLang.Parser.TyhpParser.T_IS_NOT_IDENTICAL => PhpBinaryOperator.NotIdentical,
                TyhpLang.Parser.TyhpParser.T_SYM_GT => PhpBinaryOperator.GreaterThan,
                TyhpLang.Parser.TyhpParser.T_SYM_LT => PhpBinaryOperator.LessThan,
                TyhpLang.Parser.TyhpParser.T_IS_SMALLER_OR_EQUAL => PhpBinaryOperator.LessThanOrEqual,
                TyhpLang.Parser.TyhpParser.T_IS_GREATER_OR_EQUAL => PhpBinaryOperator.GreaterThanOrEqual,
                TyhpLang.Parser.TyhpParser.T_SPACESHIP => PhpBinaryOperator.Spaceship,
                TyhpLang.Parser.TyhpParser.T_LOGICAL_AND => PhpBinaryOperator.LogicalAnd,
                TyhpLang.Parser.TyhpParser.T_LOGICAL_OR => PhpBinaryOperator.LogicalOr,
                TyhpLang.Parser.TyhpParser.T_LOGICAL_XOR => PhpBinaryOperator.LogicalXor,
                TyhpLang.Parser.TyhpParser.T_BOOLEAN_AND => PhpBinaryOperator.BooleanAnd,
                TyhpLang.Parser.TyhpParser.T_BOOLEAN_OR => PhpBinaryOperator.BooleanOr,
                TyhpLang.Parser.TyhpParser.T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG => PhpBinaryOperator.BitwiseAnd,
                TyhpLang.Parser.TyhpParser.T_SYM_PIPE => PhpBinaryOperator.BitwiseOr,
                TyhpLang.Parser.TyhpParser.T_SYM_CARET => PhpBinaryOperator.BitwiseXor,
                TyhpLang.Parser.TyhpParser.T_SL => PhpBinaryOperator.ShiftLeft,
                TyhpLang.Parser.TyhpParser.T_SR => PhpBinaryOperator.ShiftRight,
                TyhpLang.Parser.TyhpParser.T_SYM_PERIOD => PhpBinaryOperator.Concat,
                TyhpLang.Parser.TyhpParser.T_COALESCE => PhpBinaryOperator.Coalesce,
                TyhpLang.Parser.TyhpParser.T_PIPE => PhpBinaryOperator.Pipe,
                TyhpLang.Parser.TyhpParser.T_INSTANCEOF => PhpBinaryOperator.InstanceOf,
                // `is` is the Tyhp alias for `instanceof` (see Tyhp/TyhpLang/Grammar/TyhpParser.g4
                // phpExprBinaryOpGrammarAddon002: "alias of T_INSTANCEOF").
                TyhpLang.Parser.TyhpParser.T_TYHP_IS => PhpBinaryOperator.InstanceOf,
                _ => null
            };
        }
    }
} 