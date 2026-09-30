using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Postfix <c>T...</c> as a generic type argument. Valid only as the last parameter
    /// before return on built-in <c>callable</c> (<c>callable(int ...): bool</c>) —
    /// a homogeneous PHP variadic, not a pack splice and not the any-arity wildcard.
    /// </summary>
    public class TyhpPostfixEllipsisTypeAst : Base2Ast, ITypeExpression
    {
        public ITypeExpression? InnerType => Children.ElementAtOrDefault(0) as ITypeExpression;

        public static TyhpPostfixEllipsisTypeAst Create(
            ITypeExpression innerType,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpPostfixEllipsisTypeAst
            {
                Children = [innerType],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
