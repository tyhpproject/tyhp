using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef import class constant declaration (aliased identifier + optional coalesce).
    ///
    /// Grammar:
    ///   tyhpdefImportClassConstDecl
    ///     : Identifier=tyhpdefIdentifierWithOptionalAlias (T_COALESCE CoalesceExpr=expr)?
    ///     ;
    /// </summary>
    public class TyhpdefImportConstDeclAst : Base2Ast
    {
        public IBase2Ast? AliasedIdentifier => Children.ElementAtOrDefault(0);
        public IExpression? CoalesceExpr => Children.ElementAtOrDefault(1) as IExpression;

        public static TyhpdefImportConstDeclAst Create(
            IBase2Ast aliasedIdentifier,
            IExpression? coalesceExpr,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefImportConstDeclAst
            {
                Identifier = !string.IsNullOrEmpty(aliasedIdentifier.Identifier)
                    ? aliasedIdentifier.Identifier
                    : (aliasedIdentifier.ValueString ?? ""),
                Children = [aliasedIdentifier, coalesceExpr],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
