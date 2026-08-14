using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTryCatchAst : Base2Ast, IStatement
    {
        public PhpStatementBlockAst? TryBlock => Children.ElementAtOrDefault(0) as PhpStatementBlockAst;
        public PhpCatchListAst? CatchClauses => Children.ElementAtOrDefault(1) as PhpCatchListAst;
        public PhpStatementBlockAst? FinallyBlock => Children.ElementAtOrDefault(2) as PhpStatementBlockAst;
        
        public static PhpTryCatchAst Create(PhpStatementBlockAst? tryBlock, PhpCatchListAst? catchClauses, PhpStatementBlockAst? finallyBlock, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTryCatchAst
            {
                Children = [tryBlock, catchClauses, finallyBlock],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 