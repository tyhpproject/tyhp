namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Overrides the grammar addon handler for return types.
        /// The PHP visitor's handler calls the non-virtual VisitReturnTypeGrammarAddon
        /// which throws. This override dispatches to the Tyhp-specific return type guard.
        ///
        /// Grammar (PHP):
        ///   returnType
        ///     : (T_SYM_COLON TypeExpr=typeExpr)?              #returnTypeType
        ///     | returnTypeGrammarAddon                         #returnTypeGrammarAddonHandler
        ///     ;
        ///
        /// Grammar (Tyhp override):
        ///   returnTypeGrammarAddon
        ///     : T_SYM_COLON GuardVariable=T_VARIABLE (T_INSTANCEOF|T_TYHP_IS)
        ///         TypeExpr=typeExpr {this.isLanguageMode("tyhp")}?   #tyhpReturnTypeGuard
        ///     ;
        /// </summary>
        public override ITypeExpression VisitReturnTypeGrammarAddonHandler([NotNull] TyhpParser.ReturnTypeGrammarAddonHandlerContext context)
            => context.returnTypeGrammarAddon() switch
            {
                TyhpParser.TyhpReturnTypeGuardContext guard => this.VisitTyhpReturnTypeGuard(guard),
                null => ErrorAst.Create(context, GetCurrentLanguageMode(context)),
                var addon => (this.Visit(addon) as ITypeExpression)
                    ?? UnexpectedNodeAst.Create(addon)
                    ?? UnexpectedNodeAst.Create(context),
            };

        /// <summary>
        /// Visits a Tyhp return type guard: `: $variable is SomeType`
        /// Creates a TyhpReturnTypeGuardAst with the guard variable and type expression.
        ///
        /// Grammar:
        ///   T_SYM_COLON GuardVariable=T_VARIABLE (T_INSTANCEOF|T_TYHP_IS)
        ///     TypeExpr=typeExpr
        /// </summary>
        public override TyhpReturnTypeGuardAst VisitTyhpReturnTypeGuard([NotNull] TyhpParser.TyhpReturnTypeGuardContext context)
        {
            ITypeExpression typeExpr = context.TypeExpr != null
                ? this.VisitTypeExpr(context.TypeExpr)
                : ErrorAst.Create(context, GetCurrentLanguageMode(context));

            return TyhpReturnTypeGuardAst.Create(
                this.GetTokenValueAst(context, context.GuardVariable),
                typeExpr,
                context
            );
        }
    }
}
