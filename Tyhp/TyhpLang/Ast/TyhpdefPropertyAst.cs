using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a single tyhpdef property (variable name plus optional expected default).
    ///
    /// Grammar:
    ///   tyhpdefProperty
    ///     : Variable=T_VARIABLE (T_COALESCE CoalesceExpr=expr)?
    ///     ;
    /// </summary>
    public class TyhpdefPropertyAst : Base2Ast
    {
        public string VariableName => Identifier;

        public IExpression? CoalesceExpr => Children.ElementAtOrDefault(0) as IExpression;

        public static TyhpdefPropertyAst Create(
            string variableName,
            IExpression? coalesceExpr,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefPropertyAst
            {
                Identifier = variableName,
                Children = [coalesceExpr],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
