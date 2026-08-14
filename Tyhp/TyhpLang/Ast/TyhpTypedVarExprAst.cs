using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a typed variable declaration in Tyhp.
    /// Examples:
    ///   int $x = 5;
    ///   string $name;
    ///   (int) $x = &amp;$other;
    /// </summary>
    // Implements IExpression (which extends IStatement) so a typed-local declaration can appear both
    // as a statement (`int $x = 5;`) and inside a for-loop init clause (`for (int $i = 0; ...)`),
    // where the init is modeled as a PhpExpressionListAst of IExpression.
    public class TyhpTypedVarExprAst : Base2Ast, IExpression
    {
        private const short IS_REF_FLAG = -9;
        private const short IS_PARENTHESIZED_FLAG = -11;

        // Children[0]: Type expression
        // Children[1]: Variable
        // Children[2]: Assigned expression (optional)
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(0) as ITypeExpression;
        public PhpVariableAst? Variable => Children.ElementAtOrDefault(1) as PhpVariableAst;
        public IExpression? AssignedExpression => Children.ElementAtOrDefault(2) as IExpression;

        /// <summary>
        /// Whether the assignment is by reference (e.g., int $x = &amp;$other)
        /// </summary>
        public bool IsRef => HasFlag(IS_REF_FLAG);

        /// <summary>
        /// Whether the type is parenthesized (e.g., (int) $x = 5)
        /// </summary>
        public bool IsParenthesized => HasFlag(IS_PARENTHESIZED_FLAG);

        public static TyhpTypedVarExprAst Create(
            ITypeExpression? typeExpression,
            PhpVariableAst variable,
            IExpression? assignedExpression,
            bool isRef,
            bool isParenthesized,
            ParserRuleContext context,
            string? docComment = null,
            string? languageMode = null)
        {
            var result = new TyhpTypedVarExprAst
            {
                Children = [typeExpression, variable, assignedExpression],
                DocComment = docComment,
            };

            result.SetContext(context, languageMode);
            result.SetFlag(IS_REF_FLAG, isRef);
            result.SetFlag(IS_PARENTHESIZED_FLAG, isParenthesized);

            return result;
        }
    }
}
