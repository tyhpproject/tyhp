using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpGotoStatementAst : Base2Ast, IStatement
    {
        public static PhpGotoStatementAst Create(string label, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpGotoStatementAst
            {
                Identifier = label,
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
} 