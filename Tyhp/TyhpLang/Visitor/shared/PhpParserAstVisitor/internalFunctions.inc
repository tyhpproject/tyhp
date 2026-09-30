// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


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
