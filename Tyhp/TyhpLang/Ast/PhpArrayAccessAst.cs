using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArrayAccessAst : Base2Ast, IDereferenceableSuffix
    {
        public IExpression? IndexExpression => Children.ElementAtOrDefault(0) as IExpression;

        public static PhpArrayAccessAst Create(IExpression? indexExpression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpArrayAccessAst
            {
                Children = [indexExpression],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpArrayAccessAst CreateFromContext(IExpression? indexExpression, Base2Ast context)
        {
            var result = new PhpArrayAccessAst
            {
                Children = [indexExpression],
            };
            result.SetContext(context);
            return result;
        }
    }
}
