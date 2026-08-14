using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpConditionalAst : Base2Ast, IExpression, IStatement
    {
        private const short IS_MATCH_FLAG = -7;
        
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;
        public PhpConditionalArmListAst? Arms => Children.ElementAtOrDefault(1) as PhpConditionalArmListAst;
        
        public bool IsMatchSyntax => HasFlag(IS_MATCH_FLAG);
        
        public static PhpConditionalAst Create(IExpression expression, PhpConditionalArmListAst arms, bool isMatchSyntax, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpConditionalAst
            {
                Children = [expression, arms],
            };

            result.SetContext(context, languageMode);

            result.SetFlag(IS_MATCH_FLAG, isMatchSyntax);

            return result;
        }
    }
} 