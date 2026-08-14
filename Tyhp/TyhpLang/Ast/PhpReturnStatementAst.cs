using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpReturnStatementAst : Base2Ast, IStatement
    {
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;
        
        public static PhpReturnStatementAst Create(IExpression? expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpReturnStatementAst
            {
                Children = [expression],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 