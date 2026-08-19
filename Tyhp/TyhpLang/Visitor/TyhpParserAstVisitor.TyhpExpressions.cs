namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Overrides the grammar addon for unary prefix operators.
        /// Tyhp adds: T_DECIMAL_CAST, T_TYHP_AWAIT
        /// </summary>
        public override TokenValueAst? VisitPhpExprUnaryPreOpsGrammarAddon([NotNull] TyhpParser.PhpExprUnaryPreOpsGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        /// <summary>
        /// Visits a tyhpWithList: { arrayPairList }
        /// Used as the right-hand side of the 'with' binary operator with curly brace syntax.
        /// </summary>
        public override PhpArrayPairListAst VisitTyhpWithList([NotNull] TyhpParser.TyhpWithListContext context)
            => this.VisitArrayPairList(context.ArrayPairList);

        /// <summary>
        /// Overrides the grammar addon for unary postfix operators.
        /// Tyhp does not currently add new postfix operators.
        /// </summary>
        public override TokenValueAst? VisitPhpExprUnaryPostOpsGrammarAddon([NotNull] TyhpParser.PhpExprUnaryPostOpsGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        /// <summary>
        /// <c>async { ... }</c> — a Promise-valued block, not a callable.
        /// </summary>
        public override IExpression VisitPhpExprPrecBaseGrammarAddon(
            [NotNull] TyhpParser.PhpExprPrecBaseGrammarAddonContext context)
        {
            var body = context.StatementList != null
                ? this.VisitInnerStatementList(context.StatementList)
                : PhpStatementBlockAst.CreateError(context, GetCurrentLanguageMode(context));
            return TyhpAsyncBlockAst.Create(body, context, GetCurrentLanguageMode(context));
        }
    }
}