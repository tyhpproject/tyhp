// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpExpressionListAst? VisitForExprs([NotNull] TyhpParser.ForExprsContext context)
        {
            if (context.ExprList != null)
            {
                return this.VisitNonEmptyForExprs(context.ExprList);
            }
            return null;
        }
