namespace Tyhp.TyhpLang.Enum {
    [Flags]
    public enum ObjectModifier
    {
        None = 0,
        Abstract = 1,
        Final = 2,
        Readonly = 4,
    }

    public static class ObjectModifierHelper
    {
        public static ObjectModifier FromToken(Antlr4.Runtime.IToken? token)
            => FromToken(token?.Type ?? -1);

        public static ObjectModifier FromToken(int token)
            => (token) switch {
                TyhpLang.Parser.TyhpParser.T_ABSTRACT => ObjectModifier.Abstract,
                TyhpLang.Parser.TyhpParser.T_FINAL => ObjectModifier.Final,
                TyhpLang.Parser.TyhpParser.T_READONLY => ObjectModifier.Readonly,
                _ => ObjectModifier.None
            };

        public static ObjectModifier FromTokens(IEnumerable<Antlr4.Runtime.IToken?> tokens)
            => tokens.Aggregate(
                ObjectModifier.None,
                (token, result) => token | ObjectModifierHelper.FromToken(result)
            );
    }
}