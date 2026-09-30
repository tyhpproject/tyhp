// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// legacyNamespaceNameGrammarAddon
        ///     : FullyQualifiedName=T_NAME_FULLY_QUALIFIED
        ///         GenericArguments=tyhpGenericTypeArguments
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitLegacyNamespaceNameGrammarAddon([NotNull] TyhpParser.LegacyNamespaceNameGrammarAddonContext context)
            => TyhpGenericIdentifierAst.Create(
                context.FullyQualifiedName,
                this.VisitTyhpGenericTypeArguments(context.GenericArguments),
                context
            );
