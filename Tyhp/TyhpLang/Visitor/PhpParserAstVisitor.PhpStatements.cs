namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;

    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override PhpConstDeclAst VisitConstDecl([NotNull] TyhpParser.ConstDeclContext context)
        {
            var docComment = this.FindPossibleDocComment(context._findDocComment);

            return PhpConstDeclAst.Create(
                context.Identifier.Text,
                this.VisitExpr(context.ValueExpr),
                docComment,
                context
            );
        }

        public override PhpStatementBlockAst VisitInnerStatementList([NotNull] TyhpParser.InnerStatementListContext context)
            => PhpStatementBlockAst.Create(
                context._Items?.Select(this.VisitInnerStatement),
                context
            );

        public override PhpInlineOutputListAst? VisitStatementTerminal([NotNull] TyhpParser.StatementTerminalContext context)
            => context switch
            {
                _ when context.InlineOutput != null => this.VisitPhpInlineOutputStatement(context.InlineOutput),
                _ => null
            };

        public IStatement VisitInnerStatement([NotNull] TyhpParser.InnerStatementContext context)
        {
            return context switch
            {
                TyhpParser.NotAttributedInnerStatementContext notAttributedContext => this.VisitNotAttributedInnerStatement(notAttributedContext),
                TyhpParser.AttributedInnerStatementContext attributedContext => this.VisitAttributedInnerStatement(attributedContext),
                TyhpParser.InnerStatementYieldContext yieldContext => this.VisitInnerStatementYield(yieldContext),
                TyhpParser.InnerStatementGrammarAddonHandlerContext grammarAddonContext => this.VisitInnerStatementGrammarAddonHandler(grammarAddonContext),
                _ => HandleUnexpectedAlternative<IStatement>(context, "innerStatement")
            };
        }

        protected T HandleUnexpectedAlternative<T>(ParserRuleContext context, string ruleName) where T : class
        {
            this.ReportUnexpectedAlternative(context, ruleName);
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context)) as T ?? throw new InvalidOperationException($"Cannot cast ErrorAst to {typeof(T).Name}");
        }

        public override IStatement VisitNotAttributedInnerStatement([NotNull] TyhpParser.NotAttributedInnerStatementContext context)
            => this.VisitStatement(context.Statement);

        public override IStatement VisitAttributedInnerStatement([NotNull] TyhpParser.AttributedInnerStatementContext context)
        {
            var statement = this.VisitAttributedStatement(context.Statement);
            var attributes = context.Attributes != null ? this.VisitAttributes(context.Attributes) : null;
            statement.AddAttributes(attributes);
            return statement;
        }

        public override IStatement VisitInnerStatementYield([NotNull] TyhpParser.InnerStatementYieldContext context)
        {
            var statement = PhpUnaryOpAst.Create(this.GetTokenValueAst(context, context.Op), null, context);
            return this.HandleWithStatementTerminal(statement, context.statementTerminal(), context);
        }

        public override IStatement VisitInnerStatementGrammarAddonHandler([NotNull] TyhpParser.InnerStatementGrammarAddonHandlerContext context)
            => this.VisitInnerStatementGrammarAddon(context.StatementGrammarAddon);

        public override IStatement VisitInnerStatementGrammarAddon([NotNull] TyhpParser.InnerStatementGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "innerStatementGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IStatement VisitStatement([NotNull] TyhpParser.StatementContext context)
        {
            return context switch {
                _ when context.statementWithoutTerminal() != null => this.VisitStatementWithoutTerminal(context.statementWithoutTerminal()),
                _ when context.statementRequiringTerminal() != null => this.HandleWithStatementTerminal(
                    this.VisitStatementRequiringTerminal(context.statementRequiringTerminal()),
                    context.statementTerminal(),
                    context
                ),
                _ => HandleUnexpectedAlternative<IStatement>(context, "statement")
            };
        }

        public IStatement VisitStatementWithoutTerminal([NotNull] TyhpParser.StatementWithoutTerminalContext context)
        {
            if (IsErrorRecoveryContext(context))
            {
                return ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            return context switch
            {
                TyhpParser.StatementBlockContext blockContext => this.VisitStatementBlock(blockContext),
                TyhpParser.StatementIfContext ifContext => this.VisitStatementIf(ifContext),
                TyhpParser.StatementWhileContext whileContext => this.VisitStatementWhile(whileContext),
                TyhpParser.StatementForContext forContext => this.VisitStatementFor(forContext),
                TyhpParser.StatementSwitchContext switchContext => this.VisitStatementSwitch(switchContext),
                TyhpParser.StatementForeachContext foreachContext => this.VisitStatementForeach(foreachContext),
                TyhpParser.StatementDeclareContext declareContext => this.VisitStatementDeclare(declareContext),
                TyhpParser.StatementTryCatchContext tryCatchContext => this.VisitStatementTryCatch(tryCatchContext),
                TyhpParser.StatementLabelContext labelContext => this.VisitStatementLabel(labelContext),
                TyhpParser.StatementEmptyStatementContext emptyContext => this.VisitStatementEmptyStatement(emptyContext),
                TyhpParser.StatementInlineOutputContext inlineOutputContext => this.VisitStatementInlineOutput(inlineOutputContext),
                TyhpParser.StatementWithoutTerminalGrammarAddonHandlerContext grammarAddonContext => this.VisitStatementWithoutTerminalGrammarAddonHandler(grammarAddonContext),
                _ => HandleUnexpectedAlternative<IStatement>(context, "statementWithoutTerminal")
            };
        }

        public IStatement VisitStatementRequiringTerminal([NotNull] TyhpParser.StatementRequiringTerminalContext context)
        {
            // ANTLR recovery often leaves a bare StatementRequiringTerminalContext (no labeled
            // subclass) with exception / ErrorNode children. Do not walk those stubs — the parser
            // already emitted TYHP1002; visiting them only leaks TYHP2002 with ANTLR type names.
            if (IsErrorRecoveryContext(context))
            {
                return ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            return context switch
            {
                TyhpParser.StatementDoWhileContext doWhileContext => this.VisitStatementDoWhile(doWhileContext),
                TyhpParser.StatementBreakContext breakContext => this.VisitStatementBreak(breakContext),
                TyhpParser.StatementContinueContext continueContext => this.VisitStatementContinue(continueContext),
                TyhpParser.StatementReturnContext returnContext => this.VisitStatementReturn(returnContext),
                TyhpParser.StatementGlobalContext globalContext => this.VisitStatementGlobal(globalContext),
                TyhpParser.StatementStaticContext staticContext => this.VisitStatementStatic(staticContext),
                TyhpParser.StatementEchoContext echoContext => this.VisitStatementEcho(echoContext),
                TyhpParser.StatementVoidCastContext voidCastContext => this.VisitStatementVoidCast(voidCastContext),
                TyhpParser.StatementTopExprContext topExprContext => this.VisitStatementTopExpr(topExprContext),
                TyhpParser.StatementUnsetContext unsetContext => this.VisitStatementUnset(unsetContext),
                TyhpParser.StatementGotoContext gotoContext => this.VisitStatementGoto(gotoContext),
                TyhpParser.StatementAltIfContext altIfContext => this.VisitStatementAltIf(altIfContext),
                TyhpParser.StatementAltWhileContext altWhileContext => this.VisitStatementAltWhile(altWhileContext),
                TyhpParser.StatementAltForContext altForContext => this.VisitStatementAltFor(altForContext),
                TyhpParser.StatementAltForeachContext altForeachContext => this.VisitStatementAltForeach(altForeachContext),
                TyhpParser.StatementAltDeclareContext altDeclareContext => this.VisitStatementAltDeclare(altDeclareContext),
                TyhpParser.StatementAltSwitchContext altSwitchContext => this.VisitStatementAltSwitch(altSwitchContext),
                TyhpParser.StatementRequiringTerminalGrammarAddonHandlerContext grammarAddonContext => this.VisitStatementRequiringTerminalGrammarAddonHandler(grammarAddonContext),
                _ => HandleUnexpectedAlternative<IStatement>(context, "statementRequiringTerminal")
            };
        }

        public override PhpStatementBlockAst VisitStatementBlock([NotNull] TyhpParser.StatementBlockContext context)
            => this.VisitInnerStatementList(context.StatementList);

        public override PhpIfAst VisitStatementIf([NotNull] TyhpParser.StatementIfContext context)
            => this.VisitIfStmt(context.Statement);

        public override PhpLoopAst VisitStatementWhile([NotNull] TyhpParser.StatementWhileContext context)
            => PhpLoopAst.CreateWhile(this.VisitExpr(context.Expr), this.VisitStatement(context.Statement), context);

        public override PhpLoopAst VisitStatementFor([NotNull] TyhpParser.StatementForContext context)
            => PhpLoopAst.CreateFor(
                context.Statement != null ? this.VisitStatement(context.Statement) : null,
                this.VisitForInitExprs(context.ForSyntax.InitExpr),
                this.VisitForCondExprs(context.ForSyntax.TestExpr),
                this.VisitForExprs(context.ForSyntax.UpdateExpr),
                context
            );

        public override PhpConditionalAst VisitStatementSwitch([NotNull] TyhpParser.StatementSwitchContext context)
        {
            return PhpConditionalAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitCaseList(context.CaseList),
                false,
                context
            );
        }

        public override PhpLoopAst VisitStatementForeach([NotNull] TyhpParser.StatementForeachContext context)
            => PhpLoopAst.CreateForeach(
                this.VisitExpr(context.Expr),
                context.KeyVariable != null ? this.VisitForeachVariable(context.KeyVariable) : null,
                this.VisitForeachVariable(context.ValueVariable),
                this.VisitStatement(context.Statement),
                context
            );

        public override PhpDeclareAst VisitStatementDeclare([NotNull] TyhpParser.StatementDeclareContext context)
            => PhpDeclareAst.Create(
                this.VisitConstList(context.DeclareList),
                this.VisitStatement(context.Statement),
                context
            );

        public override PhpTryCatchAst VisitStatementTryCatch([NotNull] TyhpParser.StatementTryCatchContext context)
            => PhpTryCatchAst.Create(
                this.VisitInnerStatementList(context.StatementList),
                this.VisitCatchList(context.CatchList),
                this.VisitFinallyStatement(context.FinallyStatement),
                context
            );

        public override PhpLabelStatementAst VisitStatementLabel([NotNull] TyhpParser.StatementLabelContext context)
            => PhpLabelStatementAst.Create(context.Label.Text, context);

        public override PhpNopStatementAst VisitStatementEmptyStatement([NotNull] TyhpParser.StatementEmptyStatementContext context)
            => PhpNopStatementAst.Create(context);

        public override PhpInlineOutputListAst VisitStatementInlineOutput([NotNull] TyhpParser.StatementInlineOutputContext context)
            => this.VisitPhpInlineOutputStatement(context.InlineOutput) ?? PhpInlineOutputListAst.Create(null, context, GetCurrentLanguageMode(context));

        public override IStatement VisitStatementWithoutTerminalGrammarAddonHandler([NotNull] TyhpParser.StatementWithoutTerminalGrammarAddonHandlerContext context)
            => this.VisitStatementWithoutTerminalGrammarAddon(context.StatementGrammarAddon);

        public virtual IStatement VisitStatementWithoutTerminalGrammarAddon([NotNull] TyhpParser.StatementWithoutTerminalGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "statementWithoutTerminalGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpLoopAst VisitStatementDoWhile([NotNull] TyhpParser.StatementDoWhileContext context)
            => PhpLoopAst.CreateDoWhile(
                this.VisitExpr(context.Expr),
                this.VisitStatement(context.Statement),
                context
            );

        public override PhpJumpStatementAst VisitStatementBreak([NotNull] TyhpParser.StatementBreakContext context)
            => PhpJumpStatementAst.Create(PhpJumpType.Break, this.VisitOptionalExpr(context.Expr), context);

        public override PhpJumpStatementAst VisitStatementContinue([NotNull] TyhpParser.StatementContinueContext context)
            => PhpJumpStatementAst.Create(PhpJumpType.Continue, this.VisitOptionalExpr(context.Expr), context);

        public override PhpJumpStatementAst VisitStatementReturn([NotNull] TyhpParser.StatementReturnContext context)
            => PhpJumpStatementAst.Create(PhpJumpType.Return, this.VisitOptionalExpr(context.Expr), context);

        public override PhpGlobalStatementAst VisitStatementGlobal([NotNull] TyhpParser.StatementGlobalContext context)
            => PhpGlobalStatementAst.Create(this.VisitGlobalVarList(context.VariableList), context);

        public override PhpStaticStatementAst VisitStatementStatic([NotNull] TyhpParser.StatementStaticContext context)
            => PhpStaticStatementAst.Create(this.VisitStaticVarList(context.VariableList), context);

        public override PhpEchoStatementAst VisitStatementEcho([NotNull] TyhpParser.StatementEchoContext context)
            => PhpEchoStatementAst.Create(this.VisitEchoExprList(context.Expr), context);

        /// <summary>
        /// PHP 8.5 <c>(void) expr;</c> discard statement. Same unary-cast AST shape as
        /// <c>(int)</c>/<c>(string)</c>/…; not a value-producing expression (see grammar).
        /// </summary>
        public override PhpUnaryOpAst VisitStatementVoidCast([NotNull] TyhpParser.StatementVoidCastContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitExpr(context.Expr),
                true,
                context
            );

        public override IExpression VisitStatementTopExpr([NotNull] TyhpParser.StatementTopExprContext context)
            => this.VisitPhpTopExpr(context.Statement);

        public override PhpUnsetStatementAst VisitStatementUnset([NotNull] TyhpParser.StatementUnsetContext context)
            => PhpUnsetStatementAst.Create(this.VisitUnsetVariables(context.VariableList), context);

        public override PhpJumpStatementAst VisitStatementGoto([NotNull] TyhpParser.StatementGotoContext context)
            => PhpJumpStatementAst.CreateGoto(context.Label.Text, context);

        public override PhpIfAst VisitStatementAltIf([NotNull] TyhpParser.StatementAltIfContext context)
            => this.VisitAltIfStmt(context.Statement);

        public override PhpLoopAst VisitStatementAltWhile([NotNull] TyhpParser.StatementAltWhileContext context)
            => PhpLoopAst.CreateWhile(this.VisitExpr(context.Expr), this.VisitWhileStatement(context.Statement), context);

        public override PhpLoopAst VisitStatementAltFor([NotNull] TyhpParser.StatementAltForContext context)
            => PhpLoopAst.CreateFor(
                this.VisitForStatement(context.Statement),
                this.VisitForInitExprs(context.ForSyntax.InitExpr),
                this.VisitForCondExprs(context.ForSyntax.TestExpr),
                this.VisitForExprs(context.ForSyntax.UpdateExpr),
                context
            );

        public override PhpLoopAst VisitStatementAltForeach([NotNull] TyhpParser.StatementAltForeachContext context)
            => PhpLoopAst.CreateForeach(
                this.VisitExpr(context.Expr),
                context.KeyVariable != null ? this.VisitForeachVariable(context.KeyVariable) : null,
                this.VisitForeachVariable(context.ValueVariable),
                this.VisitForeachStatement(context.Statement),
                context
            );

        public override PhpDeclareAst VisitStatementAltDeclare([NotNull] TyhpParser.StatementAltDeclareContext context)
            => PhpDeclareAst.Create(
                this.VisitConstList(context.DeclareList),
                this.VisitDeclareStatement(context.Statement),
                context
            );

        public override PhpConditionalAst VisitStatementAltSwitch([NotNull] TyhpParser.StatementAltSwitchContext context)
            => PhpConditionalAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitSwitchCaseList(context.CaseList),
                false,
                context
            );

        public override IStatement VisitStatementRequiringTerminalGrammarAddonHandler([NotNull] TyhpParser.StatementRequiringTerminalGrammarAddonHandlerContext context)
            => this.VisitStatementRequiringTerminalGrammarAddon(context.StatementGrammarAddon);

        public virtual IStatement VisitStatementRequiringTerminalGrammarAddon([NotNull] TyhpParser.StatementRequiringTerminalGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "statementRequiringTerminalGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpExpressionListAst VisitUnsetVariables([NotNull] TyhpParser.UnsetVariablesContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(item =>
                    this.VisitUnsetVariable(item) as IExpression
                    ?? PhpVariableAst.CreateError(item, GetCurrentLanguageMode(item))),
                context
            );

        public override IDereferenceableBase VisitUnsetVariable([NotNull] TyhpParser.UnsetVariableContext context)
            => this.VisitVariable(context.Variable);

        public override PhpConstDeclListAst VisitConstList([NotNull] TyhpParser.ConstListContext context)
            => PhpConstDeclListAst.Create(
                context._Items.Select(this.VisitConstDecl),
                context
            );

        public override PhpExpressionListAst VisitEchoExprList([NotNull] TyhpParser.EchoExprListContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(this.VisitEchoExpr),
                context
            );

        public override IExpression VisitEchoExpr([NotNull] TyhpParser.EchoExprContext context)
            => this.VisitExpr(context.Expr);

        public IStatement VisitInternalFunctions([NotNull] TyhpParser.InternalFunctionsContext context)
        {
            return context switch {
                TyhpParser.InternalFunctionIssetContext issetContext => this.VisitInternalFunctionIsset(issetContext),
                TyhpParser.InternalFunctionEmptyContext emptyContext => this.VisitInternalFunctionEmpty(emptyContext),
                TyhpParser.InternalFunctionEvalContext evalContext => this.VisitInternalFunctionEval(evalContext),
                TyhpParser.InternalFunctionsGrammarAddonHandlerContext grammarAddonContext => this.VisitInternalFunctionsGrammarAddonHandler(grammarAddonContext),
                _ => HandleUnexpectedAlternative<IStatement>(context, "internalFunctions")
            };
        }

        public override PhpIssetStatementAst VisitInternalFunctionIsset([NotNull] TyhpParser.InternalFunctionIssetContext context)
            => PhpIssetStatementAst.Create(this.VisitIssetVariables(context.VariableList), context);

        public override PhpEmptyStatementAst VisitInternalFunctionEmpty([NotNull] TyhpParser.InternalFunctionEmptyContext context)
            => PhpEmptyStatementAst.Create(this.VisitExpr(context.Expr), context);

        public override PhpEvalStatementAst VisitInternalFunctionEval([NotNull] TyhpParser.InternalFunctionEvalContext context)
            => PhpEvalStatementAst.Create(this.VisitExpr(context.Expr), context);

        public override IStatement VisitInternalFunctionsGrammarAddonHandler([NotNull] TyhpParser.InternalFunctionsGrammarAddonHandlerContext context)
            => this.VisitInternalFunctionsGrammarAddon(context.internalFunctionsGrammarAddon());

        public virtual IStatement VisitInternalFunctionsGrammarAddon([NotNull] TyhpParser.InternalFunctionsGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "internalFunctionsGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override PhpExpressionListAst VisitIssetVariables([NotNull] TyhpParser.IssetVariablesContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(this.VisitIssetVariable),
                context
            );

        public override IExpression VisitIssetVariable([NotNull] TyhpParser.IssetVariableContext context)
            => this.VisitExpr(context.Expr);
    }
}