using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Bare <c>...</c> as a generic type argument. Valid only as the first of exactly two
    /// arguments on built-in <c>callable</c> (<c>callable(...): TReturn</c>) — a wildcard
    /// parameter list, not a rest pack.
    /// </summary>
    public class TyhpEllipsisTypeAst : Base2Ast, ITypeExpression
    {
        public static TyhpEllipsisTypeAst Create(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpEllipsisTypeAst();
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
