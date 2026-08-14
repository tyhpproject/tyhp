using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents the compile-time construct: variable_exists(expr)
    /// Checks whether a variable is defined.
    /// </summary>
    public class TyhpVariableExistsAst : Base2Ast, IExpression
    {
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;

        public static TyhpVariableExistsAst Create(IExpression expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpVariableExistsAst
            {
                Children = [expression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
