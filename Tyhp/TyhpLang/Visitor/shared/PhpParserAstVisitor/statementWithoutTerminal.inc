// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


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
