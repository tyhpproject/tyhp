using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpJumpType
    {
        None,
        Break,
        Continue,
        Return,
        Goto
    }
    
    public static class PhpJumpTypeExtensions
    {
        public static PhpJumpType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpJumpType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_BREAK => PhpJumpType.Break,
                TyhpLang.Parser.TyhpParser.T_CONTINUE => PhpJumpType.Continue,
                TyhpLang.Parser.TyhpParser.T_RETURN => PhpJumpType.Return,
                TyhpLang.Parser.TyhpParser.T_GOTO => PhpJumpType.Goto,
                _ => null
            };
    }
} 