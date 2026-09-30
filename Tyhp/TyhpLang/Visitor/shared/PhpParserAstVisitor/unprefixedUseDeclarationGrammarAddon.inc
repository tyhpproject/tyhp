// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpImportDeclAst VisitUnprefixedUseDeclarationGrammarAddon([NotNull] TyhpParser.UnprefixedUseDeclarationGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "unprefixedUseDeclarationGrammarAddon");
            return PhpImportDeclAst.Create(null, "", null, context, GetCurrentLanguageMode(context));
        }
