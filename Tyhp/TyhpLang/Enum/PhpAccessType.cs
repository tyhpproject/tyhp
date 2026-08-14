using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpAccessType
    {
        ArrayAccess,
        PropertyAccess,
        StaticPropertyAccess,
        MethodCall,
        StaticMethodCall
    }
    
    public static class PhpAccessTypeExtensions
    {
        public static PhpAccessType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpAccessType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_OPEN_SQUARE_BRACE => PhpAccessType.ArrayAccess,
                TyhpLang.Parser.TyhpParser.T_OBJECT_OPERATOR => PhpAccessType.PropertyAccess,
                TyhpLang.Parser.TyhpParser.T_NULLSAFE_OBJECT_OPERATOR => PhpAccessType.PropertyAccess,
                TyhpLang.Parser.TyhpParser.T_DOUBLE_COLON => PhpAccessType.StaticPropertyAccess,
                _ => null
            };
    }
} 