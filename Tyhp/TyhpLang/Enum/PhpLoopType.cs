using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpLoopType
    {
        None,
        While,
        DoWhile,
        For,
        Foreach
    }
    
    public static class PhpLoopTypeExtensions
    {
        public static PhpLoopType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpLoopType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_WHILE => PhpLoopType.While,
                TyhpLang.Parser.TyhpParser.T_DO => PhpLoopType.DoWhile,
                TyhpLang.Parser.TyhpParser.T_FOR => PhpLoopType.For,
                TyhpLang.Parser.TyhpParser.T_FOREACH => PhpLoopType.Foreach,
                _ => null
            };
    }
} 