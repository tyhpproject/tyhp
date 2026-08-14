using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpStatementBlockAst : NodeListAst<IStatement, PhpStatementBlockAst>, IStatement
    {
        /// <summary>
        /// Creates an error placeholder PhpStatementBlockAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpStatementBlockAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpStatementBlockAst
            {
                Children = []
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 