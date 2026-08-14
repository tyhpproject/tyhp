using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpModifier
    {
        None,
        Public,
        Protected,
        Private,
        Static,
        Abstract,
        Final,
        Readonly,
        Var,
        PublicSet,
        ProtectedSet,
        PrivateSet
    }
    
    public static class PhpModifierExtensions
    {
        public static PhpModifier FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpModifier FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_PUBLIC => PhpModifier.Public,
                TyhpLang.Parser.TyhpParser.T_PROTECTED => PhpModifier.Protected,
                TyhpLang.Parser.TyhpParser.T_PRIVATE => PhpModifier.Private,
                TyhpLang.Parser.TyhpParser.T_STATIC => PhpModifier.Static,
                TyhpLang.Parser.TyhpParser.T_ABSTRACT => PhpModifier.Abstract,
                TyhpLang.Parser.TyhpParser.T_FINAL => PhpModifier.Final,
                TyhpLang.Parser.TyhpParser.T_READONLY => PhpModifier.Readonly,
                TyhpLang.Parser.TyhpParser.T_VAR => PhpModifier.Var,
                TyhpLang.Parser.TyhpParser.T_PUBLIC_SET => PhpModifier.PublicSet,
                TyhpLang.Parser.TyhpParser.T_PROTECTED_SET => PhpModifier.ProtectedSet,
                TyhpLang.Parser.TyhpParser.T_PRIVATE_SET => PhpModifier.PrivateSet,
                _ => PhpModifier.None
            };
    }
} 