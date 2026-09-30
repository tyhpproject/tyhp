// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override IExpression VisitEchoExpr([NotNull] TyhpParser.EchoExprContext context)
            => this.VisitExpr(context.Expr);
