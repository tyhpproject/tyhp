// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpExpressionListAst VisitMatchArmCondList([NotNull] TyhpParser.MatchArmCondListContext context)
            => PhpExpressionListAst.Create(
                context._Items.Select(this.VisitExpr),
                context
            );
