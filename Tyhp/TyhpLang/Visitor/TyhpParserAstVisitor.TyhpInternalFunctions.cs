namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {







        /// <summary>
        /// Maps a PHP cast token type to the builtin type name used by the emitter's
        /// BuildDefaultExpression to select the zero value. <c>T_VOID_CAST</c> only
        /// reaches here from <c>callableType</c>'s BuiltinCast alternative (an unnamed
        /// <c>callable(void): R</c> parameter) — <c>typeof</c>/<c>default</c> do not
        /// accept <c>T_VOID_CAST</c>, so <c>default(void)</c> is not a thing this maps.
        /// </summary>
        private static string CastTokenTypeToTypeName(int tokenType) => tokenType switch
        {
            TyhpParser.T_INT_CAST => "int",
            TyhpParser.T_STRING_CAST => "string",
            TyhpParser.T_BOOL_CAST => "bool",
            TyhpParser.T_ARRAY_CAST => "array",
            TyhpParser.T_DOUBLE_CAST => "float",
            TyhpParser.T_OBJECT_CAST => "object",
            TyhpParser.T_DECIMAL_CAST => "decimal",
            TyhpParser.T_VOID_CAST => "void",
            _ => "mixed",
        };

    }
}
