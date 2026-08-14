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
            => FromToken(token?.Type ?? -1);

        public static PhpAssignmentOperator? FromToken(int token)
            => (token) switch
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