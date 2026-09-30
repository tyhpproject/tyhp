// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpExpressionListAst? VisitForCondExprs([NotNull] TyhpParser.ForCondExprsContext context)
        {
            if (context.ExprList != null)
            {
                return this.VisitNonEmptyForCondExprs(context.ExprList);
            }
            return null;
        }
