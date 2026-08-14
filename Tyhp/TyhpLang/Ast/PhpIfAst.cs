using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpIfAst : Base2Ast, IStatement
    {
        public IExpression? Condition => Children.ElementAtOrDefault(0) as IExpression;
        public IStatement? ThenStatement => Children.ElementAtOrDefault(1) as IStatement;
        public IStatement? ElseStatement => Children.ElementAtOrDefault(2) as IStatement;

        
        public static PhpIfAst Create(
            IExpression condition, 
            IStatement thenStatement, 
            IStatement? elseStatement, 
            ParserRuleContext context, 
            string? languageMode = null)
        {
            var result = new PhpIfAst
            {
                Children = [ condition, thenStatement, elseStatement ],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 