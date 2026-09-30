// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using System.Linq
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visit a typed variable declaration expression.
        ///
        /// Grammar (TyhpParser.g4):
        ///   tyhpTypedVarExpr
        ///       : TypeExpr=typeExprWithoutStatic Variable=simpleVariable
        ///           (FindDocCommentCheck=T_SYM_EQUAL IsRef=ampersand? EqualsExpr=expr)?
        ///       | T_OPEN_ROUND_BRACE TypeExpr=typeExprWithoutStatic T_CLOSE_ROUND_BRACE
        ///           Variable=simpleVariable
        ///           (FindDocCommentCheck=T_SYM_EQUAL IsRef=ampersand? EqualsExpr=expr)?
        ///       ;
        ///
        /// Examples:
        ///   int $x = 5;
        ///   string $name;
        ///   (int) $x = &amp;$other;
        /// </summary>
        public override TyhpTypedVarExprAst VisitTyhpTypedVarExpr(
            [NotNull] TyhpParser.TyhpTypedVarExprContext context)
        {
            var typeExpr = context.TypeExpr is not null
                ? this.VisitOptionalTypeWithoutStatic(context.TypeExpr)
                : null;

            PhpVariableAst variable;
            if (context.Variable != null)
            {
                variable = this.VisitSimpleVariable(context.Variable);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpTypedVarExpr.Variable");
                variable = PhpVariableAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var assignedExpr = context.EqualsExpr != null
                ? this.VisitExpr(context.EqualsExpr)
                : null;
            var isRef = context.IsRef != null;
            var isParenthesized = context.T_OPEN_ROUND_BRACE() != null;

            var docComment = context.FindDocCommentCheck != null
                ? this.FindPossibleDocComment(context.FindDocCommentCheck)
                : null;

            return TyhpTypedVarExprAst.Create(
                typeExpr,
                variable,
                assignedExpr,
                isRef,
                isParenthesized,
                context,
                docComment
            );
        }
