// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpImportDeclAst VisitInlineUseDeclaration([NotNull] TyhpParser.InlineUseDeclarationContext context)
        {
            var useType = context.UseType != null ? this.VisitUseType(context.UseType) : null;
            var useDecl = this.VisitUnprefixedUseDeclaration(context.UseDecl);
            
            // Apply the inline use type if specified
            if (useType != null) {
                useDecl.SetUseType(useType);
            }
            
            return useDecl;
        }
