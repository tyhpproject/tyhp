namespace Tyhp.TyhpLang.Enum {
    [Flags]
    public enum MemberModifier
    {
        None = 0,
        Public = 1,
        Protected = 2,
        Private = 4,
        Static = 8,
        Abstract = 16,
        Final = 32,
        Readonly = 64,
        Async = 128,
        Operator = 256,
        Var = 512,
    }

    public static class MemberModifierHelper
    {
        public static MemberModifier FromToken(Antlr4.Runtime.IToken? token)
            => FromToken(token?.Type ?? -1);

        public static MemberModifier FromToken(int token)
            => (token) switch {
                TyhpLang.Parser.TyhpParser.T_PUBLIC => MemberModifier.Public,
                TyhpLang.Parser.TyhpParser.T_PROTECTED => MemberModifier.Protected,
                TyhpLang.Parser.TyhpParser.T_PRIVATE => MemberModifier.Private,
                TyhpLang.Parser.TyhpParser.T_STATIC => MemberModifier.Static,
                TyhpLang.Parser.TyhpParser.T_ABSTRACT => MemberModifier.Abstract,
                TyhpLang.Parser.TyhpParser.T_FINAL => MemberModifier.Final,
                TyhpLang.Parser.TyhpParser.T_READONLY => MemberModifier.Readonly,
                TyhpLang.Parser.TyhpParser.T_TYHP_ASYNC => MemberModifier.Async,
                TyhpLang.Parser.TyhpParser.T_TYHP_OPERATOR => MemberModifier.Operator,
                TyhpLang.Parser.TyhpParser.T_VAR => MemberModifier.Var,
                _ => MemberModifier.None
            };
    }
}