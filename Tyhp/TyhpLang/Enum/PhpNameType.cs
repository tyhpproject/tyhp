using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpNameType
    {
        Unqualified,
        Qualified,
        FullyQualified,
        Relative
    }
    
    public static class PhpNameTypeExtensions
    {
        public static PhpNameType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpNameType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_STRING => PhpNameType.Unqualified,
                TyhpLang.Parser.TyhpParser.T_NAME_QUALIFIED => PhpNameType.Qualified,
                TyhpLang.Parser.TyhpParser.T_NAME_FULLY_QUALIFIED => PhpNameType.FullyQualified,
                TyhpLang.Parser.TyhpParser.T_NAME_RELATIVE => PhpNameType.Relative,
                _ => null
            };
    }
} 