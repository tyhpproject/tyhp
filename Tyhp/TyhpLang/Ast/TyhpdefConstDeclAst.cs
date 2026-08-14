using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef class constant declaration (identifier + optional coalesce).
    ///
    /// Grammar:
    ///   tyhpdefClassConstDecl
    ///     : Identifier=identifier (T_COALESCE CoalesceExpr=expr)?
    ///     ;
    /// </summary>
    public class TyhpdefConstDeclAst : Base2Ast
    {
        public IExpression? CoalesceExpr => Children.ElementAtOrDefault(0) as IExpression;

        public static TyhpdefConstDeclAst Create(
            string name,
            IExpression? coalesceExpr,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefConstDeclAst
            {
                Identifier = name,
                Children = [coalesceExpr],
                DocComment = docComment,
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
