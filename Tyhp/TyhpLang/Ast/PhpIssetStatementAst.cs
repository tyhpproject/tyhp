using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpIssetStatementAst : Base2Ast, IExpression
    {
        public PhpExpressionListAst? Variables => Children.ElementAtOrDefault(0) as PhpExpressionListAst;
        
        public static PhpIssetStatementAst Create(PhpExpressionListAst variables, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpIssetStatementAst
            {
                Children = [variables],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 