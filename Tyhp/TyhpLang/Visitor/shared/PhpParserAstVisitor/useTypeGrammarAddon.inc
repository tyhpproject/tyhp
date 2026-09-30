// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst VisitUseTypeGrammarAddon([NotNull] TyhpParser.UseTypeGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "useTypeGrammarAddon");
            return TokenValueAst.Create("", 0, context);
        }
