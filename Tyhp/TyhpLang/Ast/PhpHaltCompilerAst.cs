using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpHaltCompilerAst : Base2Ast, ITopStatement
    {
        public static PhpHaltCompilerAst Create(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpHaltCompilerAst();

            result.SetContext(context, languageMode);

            return result;
        }
    }
} 