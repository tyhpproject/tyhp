using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpEchoStatementAst : Base2Ast, IStatement, ITopStatement
    {
        public PhpExpressionListAst? EchoExpressions => Children.ElementAtOrDefault(0) as PhpExpressionListAst;
        
        public static PhpEchoStatementAst Create(PhpExpressionListAst expressions, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpEchoStatementAst
            {
                Children = [expressions],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 