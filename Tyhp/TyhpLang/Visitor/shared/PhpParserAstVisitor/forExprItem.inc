// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


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
