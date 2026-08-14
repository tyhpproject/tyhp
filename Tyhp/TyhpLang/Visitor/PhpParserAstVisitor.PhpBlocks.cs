namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override IForeachVariable VisitForeachVariable([NotNull] TyhpParser.ForeachVariableContext context)
        {
            if (context.Variable != null)
            {
                // Regular variable: &?$var
                return PhpVariableAst.Create(
                    this.VisitVariable(context.Variable), 
                    context.IsRef != null, 
                    context
                );
            }
            else if (context.ArrayPairList != null)
            {
                return this.VisitArrayPairList(context.ArrayPairList);
            }

            this.ReportUnexpectedAlternative(context, "foreachVariable");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpStatementBlockAst VisitForStatement([NotNull] TyhpParser.ForStatementContext context)
            => this.VisitInnerStatementList(context.StatementList);

        public override PhpStatementBlockAst VisitForeachStatement([NotNull] TyhpParser.ForeachStatementContext context)
            => this.VisitInnerStatementList(context.StatementList);

        public override PhpStatementBlockAst VisitDeclareStatement([NotNull] TyhpParser.DeclareStatementContext context)
            => this.VisitInnerStatementList(context.StatementList);

        public override PhpConditionalArmListAst VisitSwitchCaseList([NotNull] TyhpParser.SwitchCaseListContext context)
            => this.VisitCaseList(context.CaseList);

        public override PhpConditionalArmListAst VisitCaseList([NotNull] TyhpParser.CaseListContext context)
            => PhpConditionalArmListAst.Create(
                context._Items?.Select(this.VisitCaseItem),
                context
            );

        public override PhpConditionalArmAst VisitCaseItem([NotNull] TyhpParser.CaseItemContext context)
        {
            if (context.CaseExpr != null)
            {
                return this.VisitCaseExpr(context.CaseExpr);
            }
            else if (context.CaseDefault != null)
            {
                return this.VisitCaseDefault(context.CaseDefault);
            }

            this.ReportUnexpectedAlternative(context, "caseItem");
            return PhpConditionalArmAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpConditionalArmAst VisitCaseExpr([NotNull] TyhpParser.CaseExprContext context)
            => PhpConditionalArmAst.Create(
                PhpExpressionListAst.Create([this.VisitExpr(context.Expr)], context),
                PhpStatementBlockAst.Create(
                    context.StatementList._Items?.Select(this.VisitInnerStatement),
                    context
                ),
                false,
                context
            );

        public override PhpConditionalArmAst VisitCaseDefault([NotNull] TyhpParser.CaseDefaultContext context)
            => PhpConditionalArmAst.Create(
                null,
                PhpStatementBlockAst.Create(
                    context.DefaultStatementList._Items?.Select(this.VisitInnerStatement),
                    context
                ),
                true,
                context
            );

        public override IBase2Ast? VisitCaseSeparator([NotNull] TyhpParser.CaseSeparatorContext context)
            => null;

        public override PhpConditionalAst VisitMatchCheck([NotNull] TyhpParser.MatchCheckContext context)
            => PhpConditionalAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitMatchArmList(context.ArmList),
                true,
                context
            );

        public override PhpConditionalArmListAst VisitMatchArmList([NotNull] TyhpParser.MatchArmListContext context)
        {
            if (context.ArmList != null)
            {
                return this.VisitNonEmptyMatchArmList(context.ArmList);
            }
            
            return PhpConditionalArmListAst.Create(null, context);
        }

        public override PhpConditionalArmListAst VisitNonEmptyMatchArmList([NotNull] TyhpParser.NonEmptyMatchArmListContext context)
            => PhpConditionalArmListAst.Create(
                context._Items.Select(this.VisitMatchArm),
                context
            );

        public override PhpConditionalArmAst VisitMatchArm([NotNull] TyhpParser.MatchArmContext context)
        {
            if (context.IsDefault != null)
            {
                // Default arm
                return PhpConditionalArmAst.Create(
                    null,
                    PhpStatementBlockAst.Create(
                        [PhpUnaryOpAst.Create(
                            TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                            this.VisitExpr(context.Expr),
                            context
                        )],
                        context
                    ),
                    true,
                    context
                );
            }
            else
            {
                // Regular arm with conditions
                return PhpConditionalArmAst.Create(
                    this.VisitMatchArmCondList(context.ArmCondList),
                    PhpStatementBlockAst.Create(
                        [PhpUnaryOpAst.Create(
                            TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                            this.VisitExpr(context.Expr),
                            context
                        )],
                        context
                    ),
                    false,
                    context
                );
            }
        }

        public override PhpExpressionListAst VisitMatchArmCondList([NotNull] TyhpParser.MatchArmCondListContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(this.VisitExpr),
                context
            );

        public override PhpStatementBlockAst VisitWhileStatement([NotNull] TyhpParser.WhileStatementContext context)
            => this.VisitInnerStatementList(context.StatementList);

        public override PhpIfAst VisitIfStmtWithoutElse([NotNull] TyhpParser.IfStmtWithoutElseContext context)
            => this.VisitIfStmtWithoutElse(context, null);

        private PhpIfAst VisitIfStmtWithoutElse(TyhpParser.IfStmtWithoutElseContext context, PhpIfAst? chainedElse)
        {
            var ifAst = PhpIfAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitStatement(context.Statement),
                chainedElse,
                context
            );

            if (context.ChainedIfStatement != null)
            {
                return this.VisitIfStmtWithoutElse(context.ChainedIfStatement, ifAst);
            }

            return ifAst;
        }

        public override PhpIfAst VisitIfStmt([NotNull] TyhpParser.IfStmtContext context)
        {
            var ifAst = this.VisitIfStmtWithoutElse(context.IfStatement);
            if (context.ElseStatement != null)
            {
                var tail = GetIfChainTail(ifAst);
                tail.ReplaceChildAt(2, this.VisitStatement(context.ElseStatement));
            }

            return ifAst;
        }

        public override PhpIfAst VisitAltIfStmtWithoutElse([NotNull] TyhpParser.AltIfStmtWithoutElseContext context)
            => this.VisitAltIfStmtWithoutElse(context, null);

        private PhpIfAst VisitAltIfStmtWithoutElse(TyhpParser.AltIfStmtWithoutElseContext context, PhpIfAst? chainedElse)
        {
            var ifAst = PhpIfAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitInnerStatementList(context.Statement),
                chainedElse,
                context
            );

            if (context.ChainedIfStatement != null)
            {
                return this.VisitAltIfStmtWithoutElse(context.ChainedIfStatement, ifAst);
            }

            return ifAst;
        }

        public override PhpIfAst VisitAltIfStmt([NotNull] TyhpParser.AltIfStmtContext context)
        {
            var ifAst = this.VisitAltIfStmtWithoutElse(context.IfStatement);
            if (context.ElseStatement != null)
            {
                var tail = GetIfChainTail(ifAst);
                tail.ReplaceChildAt(2, this.VisitInnerStatementList(context.ElseStatement));
            }

            return ifAst;
        }

        private static PhpIfAst GetIfChainTail(PhpIfAst head)
        {
            var current = head;
            while (current.ElseStatement is PhpIfAst elseif)
            {
                current = elseif;
            }

            return current;
        }

        public override PhpExpressionListAst? VisitForExprs([NotNull] TyhpParser.ForExprsContext context)
        {
            if (context.ExprList != null)
            {
                return this.VisitNonEmptyForExprs(context.ExprList);
            }
            return null;
        }

        public override PhpExpressionListAst? VisitForCondExprs([NotNull] TyhpParser.ForCondExprsContext context)
        {
            if (context.ExprList != null)
            {
                return this.VisitNonEmptyForCondExprs(context.ExprList);
            }
            return null;
        }
        
        public override PhpExpressionListAst VisitNonEmptyForExprs([NotNull] TyhpParser.NonEmptyForExprsContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(this.VisitForExprItem),
                context
            );

        public override PhpExpressionListAst VisitNonEmptyForCondExprs([NotNull] TyhpParser.NonEmptyForCondExprsContext context)
        {
            var items = context._Items.Select(this.VisitForExprItem).Append(this.VisitExpr(context.Last));
            return PhpExpressionListAst.Create(items, context);
        }

        public IExpression VisitForExprItem([NotNull] TyhpParser.ForExprItemContext context)
            => context switch
            {
                TyhpParser.ForVoidCastExprContext voidCast => this.VisitForVoidCastExpr(voidCast),
                TyhpParser.ForPlainExprContext plain => this.VisitForPlainExpr(plain),
                _ => HandleUnexpectedAlternative<IExpression>(context, "forExprItem")
            };

        /// <summary>
        /// PHP 8.5 <c>(void) expr</c> inside a <c>for</c> init/update expr list (php-src
        /// <c>non_empty_for_exprs</c>). Same unary-cast AST as the statement form.
        /// </summary>
        public override PhpUnaryOpAst VisitForVoidCastExpr([NotNull] TyhpParser.ForVoidCastExprContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitExpr(context.Expr),
                true,
                context
            );

        public override IExpression VisitForPlainExpr([NotNull] TyhpParser.ForPlainExprContext context)
            => this.VisitExpr(context.Expr);

        // The for-init clause is overridden in TyhpParser.g4 (forSyntax) to accept typed-local
        // declarations alongside ordinary expressions, e.g. `for (int $i = 0; ...)`. The clause
        // is optional, so a null context (empty init, `for (;;)`) yields a null list.
        private PhpExpressionListAst? VisitForInitExprs(TyhpParser.TyhpForInitExprsContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return PhpExpressionListAst.Create(
                context._Items.Select(this.VisitForInitExpr),
                context
            );
        }

        private IExpression VisitForInitExpr([NotNull] TyhpParser.TyhpForInitExprContext context)
            => context switch
            {
                TyhpParser.TyhpForInitVoidCastContext voidCast => this.VisitTyhpForInitVoidCast(voidCast),
                TyhpParser.TyhpForInitPlainExprContext plain => this.VisitTyhpForInitPlainExpr(plain),
                TyhpParser.TyhpForInitTypedVarContext typed => this.VisitTyhpForInitTypedVar(typed),
                _ => HandleUnexpectedAlternative<IExpression>(context, "tyhpForInitExpr")
            };

        public override PhpUnaryOpAst VisitTyhpForInitVoidCast([NotNull] TyhpParser.TyhpForInitVoidCastContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitExpr(context.Expr),
                true,
                context
            );

        public override IExpression VisitTyhpForInitPlainExpr([NotNull] TyhpParser.TyhpForInitPlainExprContext context)
            => this.VisitExpr(context.Expr);

        public override IExpression VisitTyhpForInitTypedVar([NotNull] TyhpParser.TyhpForInitTypedVarContext context)
            => (this.Visit(context.TypedVar) as IExpression)!;
    }
}