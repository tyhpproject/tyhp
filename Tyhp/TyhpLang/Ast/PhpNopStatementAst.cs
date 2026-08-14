using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpNopStatementAst : Base2Ast, IStatement
    {
        public static PhpNopStatementAst Create(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpNopStatementAst();
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 