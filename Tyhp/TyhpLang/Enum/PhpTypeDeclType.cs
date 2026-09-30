using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpTypeDeclType
    {
        Class,
        Interface,
        Trait,
        Enum,
        /// <summary>
        /// Kind-unspecified tyhpdef <c>extern \Name;</c> placeholder. Compatible with a later
        /// real <see cref="Class"/>, <see cref="Interface"/>, or <see cref="Enum"/> of the
        /// same name. Not a PHP declaration kind.
        /// </summary>
        Unspecified
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