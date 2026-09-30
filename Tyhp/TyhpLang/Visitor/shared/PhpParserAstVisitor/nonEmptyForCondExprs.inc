// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpExpressionListAst VisitNonEmptyForCondExprs([NotNull] TyhpParser.NonEmptyForCondExprsContext context)
        {
            var items = context._Items.Select(this.VisitForExprItem).Append(this.VisitExpr(context.Last));
            return PhpExpressionListAst.Create(items, context);
        }
