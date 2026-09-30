// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpOptionalGenericIdentifierWithoutConstructor
        ///     : Identifier=identifierWithoutConstructor
        ///     | GenericIdentifier=tyhpGenericIdentifierWithoutConstructor {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitTyhpOptionalGenericIdentifierWithoutConstructor([NotNull] TyhpParser.TyhpOptionalGenericIdentifierWithoutConstructorContext context)
        {
            return context switch
            {
                _ when context.Identifier != null => this.VisitIdentifierWithoutConstructor(context.Identifier),
                _ when context.GenericIdentifier != null => this.VisitTyhpGenericIdentifierWithoutConstructor(context.GenericIdentifier),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpOptionalGenericIdentifierWithoutConstructor",
                    () => PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }
