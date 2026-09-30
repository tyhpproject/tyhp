// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmListAst VisitMatchArmList([NotNull] TyhpParser.MatchArmListContext context)
        {
            if (context.ArmList != null)
            {
                return this.VisitNonEmptyMatchArmList(context.ArmList);
            }
            
            return PhpConditionalArmListAst.Create(null, context);
        }
