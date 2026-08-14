using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpYieldAst : Base2Ast, IExpression
    {
        public IExpression? KeyExpr => Children.ElementAtOrDefault(0) as IExpression;
        public IExpression? ValueExpr => Children.ElementAtOrDefault(1) as IExpression;
        
        public static PhpYieldAst Create(IExpression? keyExpr, IExpression? valueExpr, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpYieldAst
            {
                Children = [keyExpr, valueExpr],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 