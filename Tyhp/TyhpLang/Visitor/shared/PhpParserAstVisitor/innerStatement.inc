// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


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
