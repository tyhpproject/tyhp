using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpGlobalStatementAst : Base2Ast, IStatement
    {
        public PhpExpressionListAst? Variables => Children.ElementAtOrDefault(0) as PhpExpressionListAst;
        
        public static PhpGlobalStatementAst Create(PhpVariableListAst variables, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpGlobalStatementAst
            {
                Children = [ variables ],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
} 