// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Parser


        public override Ast.PhpTopStatementListAst VisitPhpEchoBlock([NotNull] TyhpParser.PhpEchoBlockContext context)
            => this.VisitPhpEchoBlock(context, false);


        public Ast.PhpTopStatementListAst VisitPhpEchoBlock([NotNull] TyhpParser.PhpEchoBlockContext context, bool isCurrentTopStatementList)
        {
            var result = Ast.PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            if (isCurrentTopStatementList) {
                this.CurrentTopStatementList = result;
            }
            result.Add(Ast.PhpEchoStatementAst.Create(this.VisitEchoExprList(context.Expr), context));
            return result;
        }
