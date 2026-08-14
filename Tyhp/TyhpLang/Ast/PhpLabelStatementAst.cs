using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpLabelStatementAst : Base2Ast, IStatement
    {
        public static PhpLabelStatementAst Create(string label, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpLabelStatementAst
            {
                Identifier = label,
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 