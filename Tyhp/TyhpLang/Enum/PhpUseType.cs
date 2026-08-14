using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpUseType
    {
        Class,
        Function,
        Const,
        Variable
    }
    
    public static class PhpUseTypeExtensions
    {
        public static PhpUseType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpUseType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_FUNCTION => PhpUseType.Function,
                TyhpLang.Parser.TyhpParser.T_CONST => PhpUseType.Const,
                _ => PhpUseType.Class
            };
    }
} 