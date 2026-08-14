using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a single tyhpdef property (just a variable name, no value).
    ///
    /// Grammar:
    ///   tyhpdefProperty
    ///     : Variable=T_VARIABLE
    ///     ;
    /// </summary>
    public class TyhpdefPropertyAst : Base2Ast
    {
        public string VariableName => Identifier;

        public static TyhpdefPropertyAst Create(
            string variableName,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefPropertyAst
            {
                Identifier = variableName,
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
