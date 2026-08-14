namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using System.Linq;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Override the statement requiring terminal grammar addon to dispatch
        /// to the Tyhp typed variable expression handler.
        ///
        /// Grammar (TyhpParser.g4):
        ///   statementRequiringTerminalGrammarAddon
        ///       : Statement=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?   #tyhpStatementTypedVarExpr
        ///       ;
        /// </summary>
        public override IStatement VisitStatementRequiringTerminalGrammarAddon(
            [NotNull] TyhpParser.StatementRequiringTerminalGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpStatementTypedVarExprContext typedVarExpr
                    => this.VisitTyhpStatementTypedVarExpr(typedVarExpr),
                _ => base.VisitStatementRequiringTerminalGrammarAddon(context),
            };

        /// <summary>
        /// Visit the labeled alternative #tyhpStatementTypedVarExpr.
        /// Delegates to VisitTyhpTypedVarExpr for the actual typed variable expression.
        ///
        /// Grammar:
        ///   #tyhpStatementTypedVarExpr
        ///       : Statement=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?
        /// </summary>
        public override TyhpTypedVarExprAst VisitTyhpStatementTypedVarExpr(
            [NotNull] TyhpParser.TyhpStatementTypedVarExprContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpStatementTypedVarExpr.Statement");
                return TyhpTypedVarExprAst.Create(
                    PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context)),
                    PhpVariableAst.CreateError(context, GetCurrentLanguageMode(context)),
                    null,
                    false,
                    false,
                    context);
            }

            return this.VisitTyhpTypedVarExpr(context.Statement);
        }

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

        /// <summary>
        /// Visit a statement without a terminal grammar addon (tyhp-specific statements).
        /// Routes tyhpUsingBlock to the Tyhp using block handler.
        ///
        /// Grammar (TyhpParser.g4):
        ///   statementWithoutTerminalGrammarAddon
        ///       : Statement=tyhpUsingBlock {this.isLanguageMode("tyhp")}?   #tyhpStatementUsingBlock
        ///       ;
        /// </summary>
        public override IStatement VisitStatementWithoutTerminalGrammarAddon(
            [NotNull] TyhpParser.StatementWithoutTerminalGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpStatementUsingBlockContext usingCtx
                    => this.VisitTyhpUsingBlock(usingCtx.Statement),
                _ => base.VisitStatementWithoutTerminalGrammarAddon(context),
            };

        /// <summary>
        /// Visit a Tyhp using block statement.
        ///
        /// Grammar (TyhpParser.g4):
        ///   tyhpUsingBlock
        ///       : T_TYHP_USING IsAsync=T_TYHP_AWAIT?
        ///         T_OPEN_ROUND_BRACE Resources=tyhpUsingResourceList T_CLOSE_ROUND_BRACE
        ///         T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
        ///       ;
        /// </summary>
        public override TyhpUsingBlockAst VisitTyhpUsingBlock(
            [NotNull] TyhpParser.TyhpUsingBlockContext context)
        {
            var isAsync = context.IsAsync != null;
            var resources = context.Resources._Items
                .Select(r => VisitTyhpUsingResource(r))
                .ToList();
            var body = context.StatementList != null
                ? this.VisitInnerStatementList(context.StatementList)
                : null;

            return TyhpUsingBlockAst.Create(isAsync, resources, body, context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Visit an individual resource declaration within a using block.
        /// Pattern-matches on the labeled alternative subclass types generated
        /// by ANTLR for the tyhpUsingResource rule.
        /// </summary>
        private TyhpUsingResourceAst VisitTyhpUsingResource(
            [NotNull] TyhpParser.TyhpUsingResourceContext context)
        {
            return context switch
            {
                TyhpParser.TyhpUsingResourceTypedContext typed =>
                    TyhpUsingResourceAst.Create(
                        this.VisitTypeExprWithoutStatic(typed.TypeExpr),
                        this.VisitSimpleVariable(typed.Variable),
                        this.VisitExpr(typed.Expr) as IExpression
                            ?? throw new InvalidOperationException(
                                $"Using resource expression at {typed.Expr?.Start?.Line}:{typed.Expr?.Start?.Column} did not produce an IExpression"),
                        typed, GetCurrentLanguageMode(context)),

                TyhpParser.TyhpUsingResourceInferredContext inferred =>
                    TyhpUsingResourceAst.Create(
                        null,
                        this.VisitSimpleVariable(inferred.Variable),
                        this.VisitExpr(inferred.Expr) as IExpression
                            ?? throw new InvalidOperationException(
                                $"Using resource expression at {inferred.Expr?.Start?.Line}:{inferred.Expr?.Start?.Column} did not produce an IExpression"),
                        inferred, GetCurrentLanguageMode(context)),

                TyhpParser.TyhpUsingResourceUnassignedContext unassigned =>
                    TyhpUsingResourceAst.Create(
                        null, null,
                        this.VisitExpr(unassigned.Expr) as IExpression
                            ?? throw new InvalidOperationException(
                                $"Using resource expression at {unassigned.Expr?.Start?.Line}:{unassigned.Expr?.Start?.Column} did not produce an IExpression"),
                        unassigned, GetCurrentLanguageMode(context)),

                _ => throw new InvalidOperationException(
                    $"Unexpected TyhpUsingResource alternative: {context.GetType().Name}")
            };
        }
    }
}
