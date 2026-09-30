// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override IExpression VisitPhpTopExpr([NotNull] TyhpParser.PhpTopExprContext context)
            => this.VisitPhpExprPrec(context.phpExprPrec());
