// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public IExpression VisitPhpExprBase([NotNull] TyhpParser.PhpExprBaseContext context)
            => context switch {
                TyhpParser.ExprNewNonDRefContext exprNewNonDRefContext => this.VisitExprNewNonDRef(exprNewNonDRefContext),
                TyhpParser.PhpExprVariableContext phpExprVariableContext => this.VisitPhpExprVariable(phpExprVariableContext),
                TyhpParser.PhpExprScalarContext phpExprScalarContext => this.VisitPhpExprScalar(phpExprScalarContext),
                TyhpParser.PhpExprFunctionContext phpExprFunctionContext => this.VisitPhpExprFunction(phpExprFunctionContext),
                TyhpParser.PhpExprInternalFunctionContext phpExprInternalFunctionContext => this.VisitPhpExprInternalFunction(phpExprInternalFunctionContext),
                TyhpParser.PhpExprExitContext phpExprExitContext => this.VisitPhpExprExit(phpExprExitContext),
                TyhpParser.PhpExprMatchCheckContext phpExprMatchCheckContext => this.VisitPhpExprMatchCheck(phpExprMatchCheckContext),
                TyhpParser.PhpExprListContext phpExprListContext => this.VisitPhpExprList(phpExprListContext),
                TyhpParser.PhpExprPrecBaseGrammarAddonHandlerContext phpExprPrecBaseGrammarAddonHandlerContext => this.VisitPhpExprPrecBaseGrammarAddonHandler(phpExprPrecBaseGrammarAddonHandlerContext),
                _ => this.VisitPhpExprBaseAlt(context),
            };


        public virtual IExpression VisitPhpExprBaseAlt(TyhpParser.PhpExprBaseContext context)
            => (this.Visit(context) as IExpression) ?? UnexpectedNodeAst.Create(context);


        public override PhpNewAst VisitExprNewNonDRef([NotNull] TyhpParser.ExprNewNonDRefContext context)
            => this.VisitNewNonDereferenceable(context.Statement);


        public override IExpression VisitPhpExprVariable([NotNull] TyhpParser.PhpExprVariableContext context)
            => this.VisitFullyDereferenceable(context.Variable);


        public override IExpression VisitPhpExprScalar([NotNull] TyhpParser.PhpExprScalarContext context)
            => this.VisitScalar(context.Scalar);


        public override PhpInlineFunctionAst VisitPhpExprFunction([NotNull] TyhpParser.PhpExprFunctionContext context)
            => this.VisitInlineFunction(context.Function);


        public override IExpression VisitPhpExprInternalFunction([NotNull] TyhpParser.PhpExprInternalFunctionContext context)
            => (IExpression)this.VisitInternalFunctions(context.Function);


        public override PhpUnaryOpAst VisitPhpExprExit([NotNull] TyhpParser.PhpExprExitContext context)
        {
            // Bare `exit;` / `die;` — no ArgumentList. Call-like forms use full `argumentList`
            // (same shape as ctor_arguments): empty `()`, args, named args, or `...` FCC.
            // Empty `()` visits to null from VisitArgumentList; materialize an empty list so
            // bare vs empty-call remain distinguishable in the AST.
            PhpArgumentListAst? arguments = null;
            if (context.ArgumentList != null)
            {
                arguments = this.VisitArgumentList(context.ArgumentList)
                    ?? PhpArgumentListAst.Create([], context.ArgumentList);
            }

            return PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.T_EXIT().Symbol),
                arguments,
                true,
                context
            );
        }


        public override PhpConditionalAst VisitPhpExprMatchCheck([NotNull] TyhpParser.PhpExprMatchCheckContext context)
            => this.VisitMatchCheck(context.Expr);


        public override PhpArrayPairListAst VisitPhpExprList([NotNull] TyhpParser.PhpExprListContext context)
            => this.VisitArrayPairList(context.ArrayPairList);


        public override IExpression VisitPhpExprPrecBaseGrammarAddonHandler([NotNull] TyhpParser.PhpExprPrecBaseGrammarAddonHandlerContext context)
            => this.VisitPhpExprPrecBaseGrammarAddon(context.phpExprPrecBaseGrammarAddon());
