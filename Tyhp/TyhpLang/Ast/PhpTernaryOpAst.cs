using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTernaryOpAst : Base2Ast, IExpression
    {
        public TokenValueAst? Op1 => Children.ElementAtOrDefault(0) as TokenValueAst;
        public TokenValueAst? Op2 => Children.ElementAtOrDefault(1) as TokenValueAst;
        public IExpression? Condition => Children.ElementAtOrDefault(2) as IExpression;
        public IExpression? TrueExpr => Children.ElementAtOrDefault(3) as IExpression; // can be null for shortened ternary
        public IExpression? FalseExpr => Children.ElementAtOrDefault(4) as IExpression;
        
        public static PhpTernaryOpAst Create(TokenValueAst? Op1, TokenValueAst? Op2, IExpression condition, IExpression? trueExpr, IExpression falseExpr, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTernaryOpAst
            {
                Children = [Op1, Op2, condition, trueExpr, falseExpr],
            };

            result.SetContext(context, languageMode);

            return result;
        }

        /// <summary>Creates a ternary AST node for emitter synthesis (no parse context).</summary>
        internal static PhpTernaryOpAst CreateFromContext(
            TokenValueAst? op1,
            TokenValueAst? op2,
            IExpression condition,
            IExpression? trueExpr,
            IExpression falseExpr,
            Base2Ast context)
        {
            var result = new PhpTernaryOpAst
            {
                Children = [op1, op2, condition, trueExpr, falseExpr],
            };
            result.SetContext(context);
            return result;
        }
    }
}
