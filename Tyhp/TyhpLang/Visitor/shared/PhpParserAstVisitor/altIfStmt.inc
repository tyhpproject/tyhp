// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpIfAst VisitAltIfStmt([NotNull] TyhpParser.AltIfStmtContext context)
        {
            var ifAst = this.VisitAltIfStmtWithoutElse(context.IfStatement);
            if (context.ElseStatement != null)
            {
                var tail = GetIfChainTail(ifAst);
                tail.ReplaceChildAt(2, this.VisitInnerStatementList(context.ElseStatement));
            }

            return ifAst;
        }
