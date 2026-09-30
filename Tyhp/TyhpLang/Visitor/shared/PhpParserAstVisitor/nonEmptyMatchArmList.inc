// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmListAst VisitNonEmptyMatchArmList([NotNull] TyhpParser.NonEmptyMatchArmListContext context)
            => PhpConditionalArmListAst.Create(
                context._Items.Select(this.VisitMatchArm),
                context
            );
