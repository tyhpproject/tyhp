using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents the compile-time construct: typeof(expr)
    /// Returns the type name of a value as a string at compile time.
    /// </summary>
    public class TyhpTypeofAst : Base2Ast, IExpression
    {
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;

        public static TyhpTypeofAst Create(IExpression expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpTypeofAst
            {
                Children = [expression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
