// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// namespaceNameGrammarAddon
        ///     : (Name=T_STRING | QualifiedName=T_NAME_QUALIFIED)
        ///         GenericArguments=tyhpGenericTypeArguments
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitNamespaceNameGrammarAddon([NotNull] TyhpParser.NamespaceNameGrammarAddonContext context)
        {
            var genericArgs = this.VisitTyhpGenericTypeArguments(context.GenericArguments);
            var nameToken = context.Name ?? context.QualifiedName;
            if (nameToken == null)
            {
                return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }
            return TyhpGenericIdentifierAst.Create(nameToken, genericArgs, context);
        }
