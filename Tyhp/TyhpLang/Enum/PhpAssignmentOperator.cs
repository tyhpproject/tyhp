using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpAssignmentOperator
    {
        Assign,
        PlusAssign,
        MinusAssign,
        MultiplyAssign,
        DivideAssign,
        ModuloAssign,
        ConcatAssign,
        PowerAssign,
        BitwiseAndAssign,
        BitwiseOrAssign,
        BitwiseXorAssign,
        ShiftLeftAssign,
        ShiftRightAssign,
        CoalesceAssign,
        UsingEqual,
    }
    
    public static class PhpAssignmentOperatorExtensions
    {
        public static PhpAssignmentOperator? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1, token?.Text);

        /// <summary>
        /// When <paramref name="text"/> is present it is authoritative. Tyhpdef token ids are not
        /// <c>TyhpParser</c> ids; the integer is used only when the spelling is absent.
        /// </summary>
        public static PhpAssignmentOperator? FromToken(int token, string? text = null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                return text.Trim() switch
                {
                    "=" => PhpAssignmentOperator.Assign,
                    "+=" => PhpAssignmentOperator.PlusAssign,
                    "-=" => PhpAssignmentOperator.MinusAssign,
                    "*=" => PhpAssignmentOperator.MultiplyAssign,
                    "/=" => PhpAssignmentOperator.DivideAssign,
                    "%=" => PhpAssignmentOperator.ModuloAssign,
                    ".=" => PhpAssignmentOperator.ConcatAssign,
                    "**=" => PhpAssignmentOperator.PowerAssign,
                    "&=" => PhpAssignmentOperator.BitwiseAndAssign,
                    "|=" => PhpAssignmentOperator.BitwiseOrAssign,
                    "^=" => PhpAssignmentOperator.BitwiseXorAssign,
                    "<<=" => PhpAssignmentOperator.ShiftLeftAssign,
                    ">>=" => PhpAssignmentOperator.ShiftRightAssign,
                    "??=" => PhpAssignmentOperator.CoalesceAssign,
                    ":=" => PhpAssignmentOperator.UsingEqual,
                    _ => null,
                };
            }

            return token switch
            {
                TyhpLang.Parser.TyhpParser.T_SYM_EQUAL => PhpAssignmentOperator.Assign,
                TyhpLang.Parser.TyhpParser.T_PLUS_EQUAL => PhpAssignmentOperator.PlusAssign,
                TyhpLang.Parser.TyhpParser.T_MINUS_EQUAL => PhpAssignmentOperator.MinusAssign,
                TyhpLang.Parser.TyhpParser.T_MUL_EQUAL => PhpAssignmentOperator.MultiplyAssign,
                TyhpLang.Parser.TyhpParser.T_DIV_EQUAL => PhpAssignmentOperator.DivideAssign,
                TyhpLang.Parser.TyhpParser.T_MOD_EQUAL => PhpAssignmentOperator.ModuloAssign,
                TyhpLang.Parser.TyhpParser.T_CONCAT_EQUAL => PhpAssignmentOperator.ConcatAssign,
                TyhpLang.Parser.TyhpParser.T_POW_EQUAL => PhpAssignmentOperator.PowerAssign,
                TyhpLang.Parser.TyhpParser.T_AND_EQUAL => PhpAssignmentOperator.BitwiseAndAssign,
                TyhpLang.Parser.TyhpParser.T_OR_EQUAL => PhpAssignmentOperator.BitwiseOrAssign,
                TyhpLang.Parser.TyhpParser.T_XOR_EQUAL => PhpAssignmentOperator.BitwiseXorAssign,
                TyhpLang.Parser.TyhpParser.T_SL_EQUAL => PhpAssignmentOperator.ShiftLeftAssign,
                TyhpLang.Parser.TyhpParser.T_SR_EQUAL => PhpAssignmentOperator.ShiftRightAssign,
                TyhpLang.Parser.TyhpParser.T_COALESCE_EQUAL => PhpAssignmentOperator.CoalesceAssign,
                TyhpLang.Parser.TyhpParser.T_TYHP_USING_EQUAL => PhpAssignmentOperator.UsingEqual,
                _ => null
            };
        }
    }
}