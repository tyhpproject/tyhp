namespace Tyhp.TyhpLang.Enum {
    public enum OverloadableOperator
    {
        Invalid,
        Plus,
        Minus,
        Add,
        Subtract,
        Divide,
        Multiply,
        Mod,
        Increment,
        Decrement,
        Pow,
        BitwiseNot,
        BitwiseShiftLeft,
        BitwiseShiftRight,
        BitwiseOr,
        BitwiseAnd,
        BitwiseXor,
        Concat,
        CompareGreaterThan,
        CompareLessThan,
        CompareGreaterThanOrEqualTo,
        CompareLessThanOrEqualTo,
        CompareEqual,
        CompareNotEqual,
        CompareIdentical,
        CompareNotIdentical,
        CompareSpaceship,
        BooleanNot,
        IsEmpty,
        Convert,
    }

    public static class OverloadableOperatorHelper
    {
        public static OverloadableOperator FromToken(Antlr4.Runtime.IToken? token, bool isAlternateKind = false)
            => FromToken(token?.Type ?? -1, token?.Text ?? "", isAlternateKind);

        public static OverloadableOperator FromToken(int token, string text = "", bool isAlternateKind = false)
            => token switch {
                TyhpLang.Parser.TyhpParser.T_SYM_PLUS => isAlternateKind ? OverloadableOperator.Plus : OverloadableOperator.Add,
                TyhpLang.Parser.TyhpParser.T_SYM_MINUS => isAlternateKind ? OverloadableOperator.Minus : OverloadableOperator.Subtract,
                TyhpLang.Parser.TyhpParser.T_SYM_SLASH => OverloadableOperator.Divide,
                TyhpLang.Parser.TyhpParser.T_SYM_ASTERISK => OverloadableOperator.Multiply,
                TyhpLang.Parser.TyhpParser.T_SYM_PERCENT => OverloadableOperator.Mod,
                TyhpLang.Parser.TyhpParser.T_INC => OverloadableOperator.Increment,
                TyhpLang.Parser.TyhpParser.T_DEC => OverloadableOperator.Decrement,
                TyhpLang.Parser.TyhpParser.T_POW => OverloadableOperator.Pow,
                TyhpLang.Parser.TyhpParser.T_SYM_TILDE => OverloadableOperator.BitwiseNot,
                TyhpLang.Parser.TyhpParser.T_SYM_BANG => OverloadableOperator.BooleanNot,
                TyhpLang.Parser.TyhpParser.T_SL => OverloadableOperator.BitwiseShiftLeft,
                TyhpLang.Parser.TyhpParser.T_SR => OverloadableOperator.BitwiseShiftRight,
                TyhpLang.Parser.TyhpParser.T_SYM_GT => isAlternateKind ? OverloadableOperator.BitwiseShiftRight : OverloadableOperator.CompareGreaterThan,
                TyhpLang.Parser.TyhpParser.T_SYM_PERIOD => OverloadableOperator.Concat,
                TyhpLang.Parser.TyhpParser.T_SYM_LT => OverloadableOperator.CompareLessThan,
                TyhpLang.Parser.TyhpParser.T_IS_SMALLER_OR_EQUAL => OverloadableOperator.CompareLessThanOrEqualTo,
                TyhpLang.Parser.TyhpParser.T_IS_GREATER_OR_EQUAL => OverloadableOperator.CompareGreaterThanOrEqualTo,
                TyhpLang.Parser.TyhpParser.T_IS_EQUAL => OverloadableOperator.CompareEqual,
                TyhpLang.Parser.TyhpParser.T_IS_NOT_EQUAL => OverloadableOperator.CompareNotEqual,
                TyhpLang.Parser.TyhpParser.T_IS_IDENTICAL => OverloadableOperator.CompareIdentical,
                TyhpLang.Parser.TyhpParser.T_IS_NOT_IDENTICAL => OverloadableOperator.CompareNotIdentical,
                TyhpLang.Parser.TyhpParser.T_SPACESHIP => OverloadableOperator.CompareSpaceship,
                TyhpLang.Parser.TyhpParser.T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG => OverloadableOperator.BitwiseAnd,
                TyhpLang.Parser.TyhpParser.T_SYM_CARET => OverloadableOperator.BitwiseXor,
                TyhpLang.Parser.TyhpParser.T_SYM_PIPE => OverloadableOperator.BitwiseOr,
                TyhpLang.Parser.TyhpParser.T_EMPTY => OverloadableOperator.IsEmpty,
                TyhpLang.Parser.TyhpParser.T_STRING => text.ToLower().Trim() switch {
                    "empty" => OverloadableOperator.IsEmpty,
                    "convert" => OverloadableOperator.Convert,
                    _ => OverloadableOperator.Invalid
                },
                _ => OverloadableOperator.Invalid
            };

        /// <summary>
        /// Maps a compound-assignment token (<c>+=</c>, <c>-=</c>, …) to the underlying binary
        /// <see cref="OverloadableOperator"/> used for overload resolution and call-site rewriting.
        /// </summary>
        public static OverloadableOperator FromAssignmentToken(int token)
            => token switch
            {
                TyhpLang.Parser.TyhpParser.T_PLUS_EQUAL => OverloadableOperator.Add,
                TyhpLang.Parser.TyhpParser.T_MINUS_EQUAL => OverloadableOperator.Subtract,
                TyhpLang.Parser.TyhpParser.T_MUL_EQUAL => OverloadableOperator.Multiply,
                TyhpLang.Parser.TyhpParser.T_DIV_EQUAL => OverloadableOperator.Divide,
                TyhpLang.Parser.TyhpParser.T_MOD_EQUAL => OverloadableOperator.Mod,
                TyhpLang.Parser.TyhpParser.T_POW_EQUAL => OverloadableOperator.Pow,
                TyhpLang.Parser.TyhpParser.T_CONCAT_EQUAL => OverloadableOperator.Concat,
                TyhpLang.Parser.TyhpParser.T_AND_EQUAL => OverloadableOperator.BitwiseAnd,
                TyhpLang.Parser.TyhpParser.T_OR_EQUAL => OverloadableOperator.BitwiseOr,
                TyhpLang.Parser.TyhpParser.T_XOR_EQUAL => OverloadableOperator.BitwiseXor,
                TyhpLang.Parser.TyhpParser.T_SL_EQUAL => OverloadableOperator.BitwiseShiftLeft,
                TyhpLang.Parser.TyhpParser.T_SR_EQUAL => OverloadableOperator.BitwiseShiftRight,
                _ => OverloadableOperator.Invalid,
            };
    }
}