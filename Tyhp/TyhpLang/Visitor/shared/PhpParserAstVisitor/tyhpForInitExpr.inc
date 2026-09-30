// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


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
