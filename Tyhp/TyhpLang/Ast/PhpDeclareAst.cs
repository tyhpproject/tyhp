using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpDeclareAst : Base2Ast, IStatement
    {
        
        public PhpConstDeclListAst? Declarations => Children.ElementAtOrDefault(0) as PhpConstDeclListAst;
        public IStatement? Body => Children.ElementAtOrDefault(1) as IStatement;
        
        public static PhpDeclareAst Create(PhpConstDeclListAst declarations, IStatement body, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpDeclareAst
            {
                Children = [ declarations, body ],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
} 