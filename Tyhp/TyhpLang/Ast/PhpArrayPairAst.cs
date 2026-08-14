using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArrayPairAst : Base2Ast
    {
        private const short IS_EXPANSION_FLAG = -8;
        
        public IExpression? KeyExpr => Children.ElementAtOrDefault(0) as IExpression;
        public IExpression? ValueExpr => Children.ElementAtOrDefault(1) as IExpression;
        
        public bool IsExpansion => HasFlag(IS_EXPANSION_FLAG);
        
        public static PhpArrayPairAst Create(IExpression? keyExpr, IExpression valueExpr, bool isExpansion, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpArrayPairAst
            {
                Children = [keyExpr, valueExpr],
            };
            result.SetFlag(IS_EXPANSION_FLAG, isExpansion);
            result.SetContext(context, languageMode);
            return result;
        }

        public static PhpArrayPairAst CreateFromContext(
            IExpression? keyExpr,
            IExpression valueExpr,
            bool isExpansion,
            Base2Ast context)
        {
            var result = new PhpArrayPairAst
            {
                Children = [keyExpr, valueExpr],
            };
            result.SetFlag(IS_EXPANSION_FLAG, isExpansion);
            result.SetContext(context);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpArrayPairAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpArrayPairAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpArrayPairAst
            {
                Children = [null, ErrorAst.Create(context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 