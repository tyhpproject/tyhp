using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpUnsetStatementAst : Base2Ast, IStatement
    {
        public PhpExpressionListAst? Variables => Children.ElementAtOrDefault(0) as PhpExpressionListAst;
        
        public static PhpUnsetStatementAst Create(PhpExpressionListAst variables, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpUnsetStatementAst
            {
                Children = [variables],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 