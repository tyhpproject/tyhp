using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef variable import declaration.
    ///
    /// Grammar:
    ///   tyhpdefImportVariableStatement
    ///     : tyhpdefDeprecatedOrObsolete? TypeExpr=typeExprWithoutStatic
    ///         Variable=T_VARIABLE (T_AS AliasedAs=T_VARIABLE)? (T_COALESCE
    ///         CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
    ///     ;
    /// </summary>
    public class TyhpdefImportVariableAst : Base2Ast, ITopStatement
    {
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;

        public ITypeExpression? TypeExpr => Children.ElementAtOrDefault(0) as ITypeExpression;
        public IExpression? CoalesceExpr => Children.ElementAtOrDefault(1) as IExpression;

        /// <summary>
        /// The variable name (stored in Identifier).
        /// </summary>
        public string VariableName => Identifier;

        /// <summary>
        /// Optional alias variable name (stored in ValueString).
        /// </summary>
        public string? AliasedAs => ValueString;

        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);

        public static TyhpdefImportVariableAst Create(
            ITypeExpression typeExpr,
            string variableName,
            string? aliasedAs,
            IExpression? coalesceExpr,
            bool isDeprecated,
            bool isObsolete,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefImportVariableAst
            {
                Identifier = variableName,
                ValueString = aliasedAs,
                Children = [typeExpr, coalesceExpr],
                DocComment = docComment,
            };

            result.SetFlag(IS_DEPRECATED_FLAG, isDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, isObsolete);
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
