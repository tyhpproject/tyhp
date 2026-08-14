using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpCatchClauseAst : Base2Ast
    {
        public PhpClassNameListAst? ExceptionTypes => Children.ElementAtOrDefault(0) as PhpClassNameListAst;
        public PhpVariableAst? Variable => Children.ElementAtOrDefault(1) as PhpVariableAst;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(2) as PhpStatementBlockAst;
        
        public static PhpCatchClauseAst Create(PhpClassNameListAst exceptionTypes, PhpVariableAst? variable, PhpStatementBlockAst body, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpCatchClauseAst
            {
                Children = [exceptionTypes, variable, body],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
} 