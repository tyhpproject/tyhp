using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpTypeDeclType
    {
        Class,
        Interface,
        Trait,
        Enum
    }
    
    public static class PhpTypeDeclTypeExtensions
    {
        public static PhpTypeDeclType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpTypeDeclType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_CLASS => PhpTypeDeclType.Class,
                TyhpLang.Parser.TyhpParser.T_INTERFACE => PhpTypeDeclType.Interface,
                TyhpLang.Parser.TyhpParser.T_TRAIT => PhpTypeDeclType.Trait,
                TyhpLang.Parser.TyhpParser.T_ENUM => PhpTypeDeclType.Enum,
                _ => null
            };
    }
} 