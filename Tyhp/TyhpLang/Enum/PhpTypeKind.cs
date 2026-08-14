using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpTypeKind
    {
        Simple,
        Union,
        Intersection,
        Invalid
    }
    
    public static class PhpTypeKindExtensions
    {
        public static PhpTypeKind? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpTypeKind? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_SYM_PIPE => PhpTypeKind.Union,
                TyhpLang.Parser.TyhpParser.T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG => PhpTypeKind.Intersection,
                _ => PhpTypeKind.Simple
            };
    }
} 