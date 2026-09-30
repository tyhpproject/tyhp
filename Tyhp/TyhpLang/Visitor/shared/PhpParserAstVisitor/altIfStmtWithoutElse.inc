// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpIfAst VisitAltIfStmtWithoutElse([NotNull] TyhpParser.AltIfStmtWithoutElseContext context)
            => this.VisitAltIfStmtWithoutElse(context, null);


        private PhpIfAst VisitAltIfStmtWithoutElse(TyhpParser.AltIfStmtWithoutElseContext context, PhpIfAst? chainedElse)
        {
            var ifAst = PhpIfAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitInnerStatementList(context.Statement),
                chainedElse,
                context
            );

            if (context.ChainedIfStatement != null)
            {
                return this.VisitAltIfStmtWithoutElse(context.ChainedIfStatement, ifAst);
            }

            return ifAst;
        }
