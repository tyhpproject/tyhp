using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpEmptyStatementAst : Base2Ast, IExpression
    {
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;
        
        public static PhpEmptyStatementAst Create(IExpression expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpEmptyStatementAst
            {
                Children = [expression],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 