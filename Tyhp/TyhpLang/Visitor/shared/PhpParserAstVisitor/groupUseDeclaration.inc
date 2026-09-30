// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpImportDeclListAst VisitGroupUseDeclaration([NotNull] TyhpParser.GroupUseDeclarationContext context)
        {
            var namespaceName = this.VisitLegacyNamespaceName(context.NamespaceName);
            var useDeclList = this.VisitUnprefixedUseDeclarations(context.UseDeclList);
            
            var prefix = namespaceName.ValueString + (context.NsSep?.Text ?? "");
            
            // Create new import declarations with the prefix applied
            var newItems = new List<PhpImportDeclAst>();
            foreach (var useDecl in useDeclList.GetAllNotNull()) {
                var prefixedNamespace = prefix + useDecl.NamespaceName;
                var newImport = PhpImportDeclAst.Create(
                    useDecl.UseType,
                    prefixedNamespace,
                    useDecl.Identifier, // Keep the alias if any
                    context,
                    GetCurrentLanguageMode(context)
                );
                newItems.Add(newImport);
            }
            
            return PhpImportDeclListAst.Create(newItems, context, GetCurrentLanguageMode(context));
        }
