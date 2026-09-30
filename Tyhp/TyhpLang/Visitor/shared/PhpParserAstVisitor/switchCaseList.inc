// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmListAst VisitSwitchCaseList([NotNull] TyhpParser.SwitchCaseListContext context)
            => this.VisitCaseList(context.CaseList);
