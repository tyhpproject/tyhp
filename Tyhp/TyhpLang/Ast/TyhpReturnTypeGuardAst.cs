using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp return type guard: <c>: $variable is SomeType</c> or
    /// <c>: $array[$key] is SomeType</c>.
    /// Used in function/method return type position to narrow the type of a subject.
    /// Grammar: <c>T_SYM_COLON GuardVariable=T_VARIABLE ('[' index ']')? (T_INSTANCEOF|T_TYHP_IS) TypeExpr=typeExpr</c>
    /// </summary>
    public class TyhpReturnTypeGuardAst : Base2Ast, ITypeExpression
    {
        /// <summary>
        /// Guard subject: a bare <c>$param</c> or an index read <c>$array[$key]</c>.
        /// Legacy cache trees may store a <see cref="TokenValueAst"/> here instead; those
        /// surface through <see cref="GuardVariable"/> only.
        /// </summary>
        public IExpression? GuardSubject => Children.ElementAtOrDefault(0) as IExpression;

        /// <summary>
        /// The guard variable token when the subject is a bare <c>$param</c>.
        /// Null for index-access subjects. Also reads legacy trees that stored the
        /// token directly as child 0.
        /// </summary>
        public TokenValueAst? GuardVariable => Children.ElementAtOrDefault(0) switch
        {
            TokenValueAst token => token,
            PhpVariableAst variable => variable.VariableToken,
            _ => null,
        };

        /// <summary>
        /// The type expression that the subject is narrowed to.
        /// </summary>
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(1) as ITypeExpression;

        public static TyhpReturnTypeGuardAst Create(
            TokenValueAst guardVariable,
            ITypeExpression typeExpression,
            ParserRuleContext context,
            string? languageMode = null) =>
            Create(
                PhpVariableAst.Create(guardVariable, false, context, languageMode),
                typeExpression,
                context,
                languageMode);

        public static TyhpReturnTypeGuardAst Create(
            IExpression guardSubject,
            ITypeExpression typeExpression,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpReturnTypeGuardAst
            {
                Children = [guardSubject, typeExpression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
