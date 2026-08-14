using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpStaticStatementAst : Base2Ast, IStatement
    {
        public PhpVariableListAst? Variables => Children.ElementAtOrDefault(0) as PhpVariableListAst;
        
        public static PhpStaticStatementAst Create(PhpVariableListAst variables, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpStaticStatementAst
            {
                Children = [variables],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 