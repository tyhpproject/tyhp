using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArrayAst : Base2Ast, IExpression
    {
        private const short IS_SHORT_SYNTAX_FLAG = -4;
        
        public PhpArrayPairListAst? ArrayPairs => Children.Count > 0 ? Children[0] as PhpArrayPairListAst : null;
        
        public bool IsShortSyntax => HasFlag(IS_SHORT_SYNTAX_FLAG);
        
        public static PhpArrayAst Create(PhpArrayPairListAst arrayPairs, bool isShortSyntax, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpArrayAst
            {
                Children = [arrayPairs],
            };
            result.SetFlag(IS_SHORT_SYNTAX_FLAG, isShortSyntax);
            result.SetContext(context, languageMode);
            return result;
        }

        public static PhpArrayAst CreateFromContext(PhpArrayPairListAst arrayPairs, bool isShortSyntax, Base2Ast context)
        {
            var result = new PhpArrayAst
            {
                Children = [arrayPairs],
            };
            result.SetFlag(IS_SHORT_SYNTAX_FLAG, isShortSyntax);
            result.SetContext(context);
            return result;
        }
    }
} 