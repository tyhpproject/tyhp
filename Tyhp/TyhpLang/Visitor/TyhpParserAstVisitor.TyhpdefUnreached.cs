namespace Tyhp.TyhpLang.Visitor
{
    using System;
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Parser;

    /// <summary>
    /// Visits for <c>tyhpdefClassConstDecl</c> / <c>tyhpdefClassConstList</c> — the two Tyhp
    /// rules the tyhpdef closure does not reach (see Story 27.2.1's appendix, "Not in the
    /// tyhpdef grammar"). <see cref="Parser.TyhpdefParser"/> has no matching context types for
    /// these, so unlike the rest of the closure they cannot be shared visit methods; they stay
    /// only on <see cref="TyhpParserAstVisitor"/>.
    /// </summary>
    public partial class TyhpParserAstVisitor
    {
        /// <summary>
        /// Visits a tyhpdef class const declaration.
        /// Grammar: tyhpdefClassConstDecl
        ///   : Identifier=identifier (T_COALESCE CoalesceExpr=expr)?
        /// </summary>
        public override TyhpdefConstDeclAst VisitTyhpdefClassConstDecl([NotNull] TyhpParser.TyhpdefClassConstDeclContext context)
        {
            var name = this.VisitIdentifier(context.Identifier);
            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;
            var docComment = context._findDocComment != null
                ? this.FindPossibleDocComment(context._findDocComment)
                : null;

            return TyhpdefConstDeclAst.Create(
                name.ValueString ?? "",
                coalesceExpr,
                docComment,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef class const list.
        /// Grammar: tyhpdefClassConstList
        ///   : Items+=tyhpdefClassConstDecl (T_SYM_COMMA Items+=tyhpdefClassConstDecl)*
        /// </summary>
        public override TyhpdefConstDeclListAst VisitTyhpdefClassConstList([NotNull] TyhpParser.TyhpdefClassConstListContext context)
            => TyhpdefConstDeclListAst.Create(
                context._Items.Select(this.VisitTyhpdefClassConstDecl),
                context
            );
    }
}
