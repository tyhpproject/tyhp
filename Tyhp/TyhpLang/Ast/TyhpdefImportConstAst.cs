using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef constant import declaration.
    ///
    /// Grammar:
    ///   tyhpdefImportConstStatement
    ///     : tyhpdefDeprecatedOrObsolete? T_CONST TypeExpr=typeExprWithoutStatic
    ///         (AliasedIdentifier=tyhpdefIdentifierWithOptionalAlias | Identifier=name)
    ///         (T_COALESCE CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
    ///     ;
    /// </summary>
    public class TyhpdefImportConstAst : Base2Ast, ITopStatement
    {
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;

        public ITypeExpression? TypeExpr => Children.ElementAtOrDefault(0) as ITypeExpression;
        public IBase2Ast? NameOrAlias => Children.ElementAtOrDefault(1);
        public IExpression? CoalesceExpr => Children.ElementAtOrDefault(2) as IExpression;

        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);

        public static TyhpdefImportConstAst Create(
            ITypeExpression typeExpr,
            IBase2Ast nameOrAlias,
            IExpression? coalesceExpr,
            bool isDeprecated,
            bool isObsolete,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefImportConstAst
            {
                Identifier = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                    ? nameOrAlias.Identifier
                    : (nameOrAlias.ValueString ?? ""),
                Children = [typeExpr, nameOrAlias, coalesceExpr],
                DocComment = docComment,
            };

            result.SetFlag(IS_DEPRECATED_FLAG, isDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, isObsolete);
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
