using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// AST node for an individual resource declaration within a using block.
    /// Supports three forms:
    ///   1. Typed: DatabaseConnection $db = new DatabaseConnection()
    ///   2. Inferred: $db = new DatabaseConnection()
    ///   3. Unassigned: new TempFile("/tmp/work")
    /// </summary>
    public class TyhpUsingResourceAst : Base2Ast
    {
        private const short HAS_TYPE_FLAG = 7200;
        private const short HAS_VARIABLE_FLAG = 7201;

        /// <summary>Whether this resource has an explicit type annotation.</summary>
        public bool HasTypeAnnotation => HasFlag(HAS_TYPE_FLAG);

        /// <summary>Whether this resource has a variable assignment.</summary>
        public bool HasVariable => HasFlag(HAS_VARIABLE_FLAG);

        /// <summary>Type annotation (if present). Null for inferred/unassigned resources.</summary>
        public IBase2Ast? TypeExpr => HasTypeAnnotation ? Children.ElementAtOrDefault(0) : null;

        /// <summary>Variable being assigned to (if present). Null for unassigned resources.</summary>
        public IBase2Ast? Variable => HasVariable
            ? Children.ElementAtOrDefault(HasTypeAnnotation ? 1 : 0)
            : null;

        /// <summary>The resource expression (always present).</summary>
        public IExpression? Expression => Children.LastOrDefault() as IExpression;

        /// <summary>Creates a new TyhpUsingResourceAst.</summary>
        public static TyhpUsingResourceAst Create(
            IBase2Ast? typeExpr,
            IBase2Ast? variable,
            IExpression expression,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var children = new List<IBase2Ast>();
            if (typeExpr != null) children.Add(typeExpr);
            if (variable != null) children.Add(variable);
            children.Add(expression);

            var result = new TyhpUsingResourceAst
            {
                Children = children,
            };
            result.SetContext(context, languageMode);
            if (typeExpr != null) result.SetFlag(HAS_TYPE_FLAG, true);
            if (variable != null) result.SetFlag(HAS_VARIABLE_FLAG, true);
            return result;
        }
    }
}
