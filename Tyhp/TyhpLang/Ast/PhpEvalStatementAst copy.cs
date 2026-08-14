using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpEvalStatementAst : Base2Ast, IExpression
    {
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;
        
        public static PhpEvalStatementAst Create(IExpression expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpEvalStatementAst
            {
                Children = [expression],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 