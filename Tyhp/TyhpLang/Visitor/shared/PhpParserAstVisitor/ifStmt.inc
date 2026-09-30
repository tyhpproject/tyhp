// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpIfAst VisitIfStmt([NotNull] TyhpParser.IfStmtContext context)
        {
            var ifAst = this.VisitIfStmtWithoutElse(context.IfStatement);
            if (context.ElseStatement != null)
            {
                var tail = GetIfChainTail(ifAst);
                tail.ReplaceChildAt(2, this.VisitStatement(context.ElseStatement));
            }

            return ifAst;
        }
