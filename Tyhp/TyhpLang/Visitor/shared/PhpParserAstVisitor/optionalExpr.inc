// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override IExpression? VisitOptionalExpr([NotNull] TyhpParser.OptionalExprContext context)
            => context.Expr != null ? this.VisitExpr(context.Expr) : null;
