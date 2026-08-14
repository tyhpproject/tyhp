using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArgumentAst : Base2Ast
    {
        private const short IS_VARIADIC_FLAG = -10;
        
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;
        public TokenValueAst? Name => Children.ElementAtOrDefault(1) as TokenValueAst;
        
        public bool IsVariadic => HasFlag(IS_VARIADIC_FLAG);

        public static PhpArgumentAst Create(IExpression? argumentExpression, ParserRuleContext context, string? languageMode = null)
            => Create(argumentExpression, null, false, context, languageMode);
        
        public static PhpArgumentAst Create(IExpression? argumentExpression, TokenValueAst? argumentName, bool isVariadic, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpArgumentAst
            {
                Children = [argumentExpression, argumentName],
            };
            result.SetFlag(IS_VARIADIC_FLAG, isVariadic);
            
            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpArgumentAst CreateFromContext(IExpression? expression, Base2Ast context)
        {
            var result = new PhpArgumentAst
            {
                Children = [expression, null],
            };
            result.SetContext(context);
            return result;
        }

        /// <summary>Builds a <c>name: expr</c> argument for emitter-synthesized calls.</summary>
        internal static PhpArgumentAst CreateNamedFromContext(
            IExpression? expression,
            string name,
            Base2Ast context)
        {
            var result = new PhpArgumentAst
            {
                Children = [expression, PhpNameAst.CreateFromContext(name, context)],
            };
            result.SetContext(context);
            return result;
        }
    }
} 