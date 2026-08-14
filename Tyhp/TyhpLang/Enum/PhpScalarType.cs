using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Enum
{
    public enum PhpScalarType
    {
        Integer,
        Float,
        OctalNumber,
        HexNumber,
        BinaryNumber,
        String
    }
    
    public static class PhpScalarTypeExtensions
    {
        public static PhpScalarType? FromToken(IToken? token)
            => FromToken(token?.Type ?? -1);

        public static PhpScalarType? FromToken(int token)
            => (token) switch
            {
                TyhpLang.Parser.TyhpParser.T_LNUMBER => PhpScalarType.Integer,
                TyhpLang.Parser.TyhpParser.T_DNUMBER => PhpScalarType.Float,
                TyhpLang.Parser.TyhpParser.T_ONUMBER => PhpScalarType.OctalNumber,
                TyhpLang.Parser.TyhpParser.T_HNUMBER => PhpScalarType.HexNumber,
                TyhpLang.Parser.TyhpParser.T_BNUMBER => PhpScalarType.BinaryNumber,
                TyhpLang.Parser.TyhpParser.T_CONSTANT_ENCAPSED_STRING => PhpScalarType.String,
                _ => null
            };
    }
} 