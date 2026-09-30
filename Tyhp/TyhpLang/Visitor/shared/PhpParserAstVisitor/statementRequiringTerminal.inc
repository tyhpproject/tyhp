// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


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
