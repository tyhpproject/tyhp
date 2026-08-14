using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp return type guard: `: $variable is SomeType`
    /// Used in function/method return type position to narrow the type of a variable.
    /// Grammar: T_SYM_COLON GuardVariable=T_VARIABLE (T_INSTANCEOF|T_TYHP_IS) TypeExpr=typeExpr
    /// </summary>
    public class TyhpReturnTypeGuardAst : Base2Ast, ITypeExpression
    {
        /// <summary>
        /// The guard variable token (e.g., $x)
        /// </summary>
        public TokenValueAst? GuardVariable => Children.ElementAtOrDefault(0) as TokenValueAst;

        /// <summary>
        /// The type expression that the variable is narrowed to
        /// </summary>
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(1) as ITypeExpression;

        public static TyhpReturnTypeGuardAst Create(
            TokenValueAst guardVariable,
            ITypeExpression typeExpression,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpReturnTypeGuardAst
            {
                Children = [guardVariable, typeExpression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
