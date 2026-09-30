// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmListAst VisitCaseList([NotNull] TyhpParser.CaseListContext context)
            => PhpConditionalArmListAst.Create(
                context._Items?.Select(this.VisitCaseItem),
                context
            );
