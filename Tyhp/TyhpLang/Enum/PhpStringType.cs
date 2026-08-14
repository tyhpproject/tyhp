using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpStringType
    {
        SingleQuoted,
        DoubleQuoted,
        BinaryDoubleQuoted,
        Heredoc,
        BackQuoted,
        BinaryBackQuoted
    }
    
    public static class PhpStringTypeExtensions
    {
        public static PhpStringType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpStringType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_CONSTANT_ENCAPSED_STRING => PhpStringType.SingleQuoted,
                TyhpLang.Parser.TyhpParser.T_DOUBLE_QUOTE => PhpStringType.DoubleQuoted,
                TyhpLang.Parser.TyhpParser.T_BINARY_DOUBLE_QUOTE => PhpStringType.BinaryDoubleQuoted,
                TyhpLang.Parser.TyhpParser.T_START_HEREDOC => PhpStringType.Heredoc,
                TyhpLang.Parser.TyhpParser.T_BINARY_BACKQUOTE => PhpStringType.BinaryBackQuoted,
                TyhpLang.Parser.TyhpParser.T_BACKQUOTE => PhpStringType.BackQuoted,
                _ => null
            };
    }
} 