using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpBinaryOpAst : Base2Ast, IExpression
    {
        public IExpression? Left => Children.ElementAtOrDefault(1) as IExpression;
        public IExpression? Right => Children.ElementAtOrDefault(2) as IExpression;
        public TokenValueAst? Operator => Children.ElementAtOrDefault(0) as TokenValueAst;

        public static PhpBinaryOpAst Create(TokenValueAst op, IExpression left, IExpression right, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpBinaryOpAst
            {
                Children = [op, left, right],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpBinaryOpAst CreateFromContext(
            TokenValueAst op,
            IExpression left,
            IExpression right,
            Base2Ast context)
        {
            var result = new PhpBinaryOpAst
            {
                Children = [op, left, right],
            };
            result.SetContext(context);
            return result;
        }

    }
} 