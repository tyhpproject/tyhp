using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpMemberAccessAst : Base2Ast, IExpression, IDereferenceableSuffix
    {
        public IExpression? Target => Children.ElementAtOrDefault(0) as IExpression;
        public IExpression? Key => Children.ElementAtOrDefault(1) as IExpression;
        public IMemberAccessor? Accessor => Children.ElementAtOrDefault(2) as IMemberAccessor;
        
        public static PhpMemberAccessAst Create(IExpression? target, IMemberAccessor accessType, IExpression? key, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpMemberAccessAst
            {
                Children = [target, key, accessType],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 