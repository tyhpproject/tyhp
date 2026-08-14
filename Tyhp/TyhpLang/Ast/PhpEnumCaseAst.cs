using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpEnumCaseAst : Base2Ast, IClassMember
    {
        public IExpression? Value => Children.ElementAtOrDefault(1) as IExpression;
        public PhpNameAst? Name => Children.ElementAtOrDefault(0) as PhpNameAst;
        
        public static PhpEnumCaseAst Create(PhpNameAst name, IExpression? value, string? docComment, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpEnumCaseAst
            {
                Children = [ name, value ],
                DocComment = docComment,
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
} 