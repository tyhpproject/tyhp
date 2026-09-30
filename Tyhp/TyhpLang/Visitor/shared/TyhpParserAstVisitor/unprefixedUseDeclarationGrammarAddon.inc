// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Overrides the unprefixed use declaration grammar addon for Tyhp generics.
        ///
        /// Grammar (TyhpParser.g4):
        ///   unprefixedUseDeclarationGrammarAddon
        ///     : NamespaceName=namespaceName
        ///         (T_AS AliasedAs=T_STRING GenericArguments=tyhpGenericTypeArguments)
        ///     ;
        ///
        /// Syntax: `use Foo\Bar as Alias&lt;T, U&gt;`
        ///
        /// Creates a PhpImportDeclAst with the generic arguments attached
        /// as a grammar addon under the key "GenericArguments".
        /// </summary>
        public override PhpImportDeclAst VisitUnprefixedUseDeclarationGrammarAddon([NotNull] TyhpParser.UnprefixedUseDeclarationGrammarAddonContext context)
        {
            var namespaceName = this.VisitNamespaceName(context.NamespaceName);
            var aliasedAs = context.AliasedAs?.Text;
            var genericArguments = this.VisitTyhpGenericTypeArguments(context.GenericArguments);

            var result = PhpImportDeclAst.Create(
                null, // Use type will be set at higher level if needed
                namespaceName.ValueString,
                aliasedAs,
                context,
                GetCurrentLanguageMode(context)
            );

            result.AddGrammarAddon("GenericArguments", genericArguments);

            return result;
        }
