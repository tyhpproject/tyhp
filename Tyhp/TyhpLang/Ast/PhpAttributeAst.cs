using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpAttributeAst : Base2Ast
    {
        public IExpression? Name => Children.ElementAtOrDefault(0) as IExpression;
        public PhpArgumentListAst? Arguments => Children.ElementAtOrDefault(1) as PhpArgumentListAst;
        
        public static PhpAttributeAst Create(IExpression name, PhpArgumentListAst? arguments, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpAttributeAst
            {
                Children = [name, arguments ?? PhpArgumentListAst.Create(null, context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 