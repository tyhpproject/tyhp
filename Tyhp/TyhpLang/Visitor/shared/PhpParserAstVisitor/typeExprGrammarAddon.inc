// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpTypeExpressionAst VisitTypeExprGrammarAddon([NotNull] TyhpParser.TypeExprGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "typeExprGrammarAddon");
            return PhpTypeExpressionAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
