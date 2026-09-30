// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalAst VisitMatchCheck([NotNull] TyhpParser.MatchCheckContext context)
            => PhpConditionalAst.Create(
                this.VisitExpr(context.Expr),
                this.VisitMatchArmList(context.ArmList),
                true,
                context
            );
