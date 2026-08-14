using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpDereferenceableExpressionAst : Base2Ast, IDereferenceableBase
    {
        
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;
        
        public static PhpDereferenceableExpressionAst Create(IExpression expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpDereferenceableExpressionAst
            {
                Children = [ expression ],
            };

            result.SetContext(context, languageMode);

            return result;
        }

        internal static PhpDereferenceableExpressionAst CreateFromContext(IExpression expression, Base2Ast context)
        {
            var result = new PhpDereferenceableExpressionAst
            {
                Children = [ expression ],
            };

            result.SetContext(context);

            return result;
        }
    }
} 