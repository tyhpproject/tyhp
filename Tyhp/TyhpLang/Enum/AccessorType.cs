namespace Tyhp.TyhpLang.Enum {
    public enum AccessorType
    {
        Invalid,
        Get,
        Set,
        Lazy,
        Guard,
        Isset,
        Unset,
    }

    public static class AccessorTypeHelper
    {
        public static AccessorType FromToken(Antlr4.Runtime.IToken? token)
            => FromToken(token?.Type ?? -1);

        public static AccessorType FromToken(int token)
            => (token) switch {
                // TyhpLang.Parser.TyhpParser.T_TYHP_PROP_ACCESSOR_GET => AccessorType.Get,
                // TyhpLang.Parser.TyhpParser.T_TYHP_PROP_ACCESSOR_SET => AccessorType.Set,
                // TyhpLang.Parser.TyhpParser.T_TYHP_PROP_ACCESSOR_LAZY => AccessorType.Lazy,
                // TyhpLang.Parser.TyhpParser.T_TYHP_PROP_ACCESSOR_GUARD => AccessorType.Guard,
                // TyhpLang.Parser.TyhpParser.T_ISSET => AccessorType.Isset,
                // TyhpLang.Parser.TyhpParser.T_UNSET => AccessorType.Unset,
                _ => AccessorType.Invalid
            };
    }
}