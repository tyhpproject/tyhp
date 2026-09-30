// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpIfAst VisitIfStmtWithoutElse([NotNull] TyhpParser.IfStmtWithoutElseContext context)
            => this.VisitIfStmtWithoutElse(context, null);


        private PhpIfAst VisitIfStmtWithoutElse(TyhpParser.IfStmtWithoutElseContext context, PhpIfAst? chainedElse)
        {
            var ifAst = PhpIfAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitStatement(context.Statement),
                chainedElse,
                context
            );

            if (context.ChainedIfStatement != null)
            {
                return this.VisitIfStmtWithoutElse(context.ChainedIfStatement, ifAst);
            }

            return ifAst;
        }
