// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpImportDeclAst VisitUseDeclaration([NotNull] TyhpParser.UseDeclarationContext context)
        {
            if (context.NamespaceName != null) {
                var namespaceName = this.VisitLegacyNamespaceName(context.NamespaceName);
                var aliasedAs = context.AliasedAs?.Text;
                
                return PhpImportDeclAst.Create(
                    null, // Use type will be set at higher level if needed
                    namespaceName.ValueString,
                    aliasedAs,
                    context,
                    GetCurrentLanguageMode(context)
                );
            } else if (context.useDeclarationGrammarAddon() != null) {
                return this.VisitUseDeclarationGrammarAddon(context.useDeclarationGrammarAddon());
            }

            this.ReportUnexpectedAlternative(context, "useDeclaration");
            return PhpImportDeclAst.Create(null, "", null, context, GetCurrentLanguageMode(context));
        }
