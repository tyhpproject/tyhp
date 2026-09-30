// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmAst VisitCaseItem([NotNull] TyhpParser.CaseItemContext context)
        {
            if (context.CaseExpr != null)
            {
                return this.VisitCaseExpr(context.CaseExpr);
            }
            else if (context.CaseDefault != null)
            {
                return this.VisitCaseDefault(context.CaseDefault);
            }

            this.ReportUnexpectedAlternative(context, "caseItem");
            return PhpConditionalArmAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
