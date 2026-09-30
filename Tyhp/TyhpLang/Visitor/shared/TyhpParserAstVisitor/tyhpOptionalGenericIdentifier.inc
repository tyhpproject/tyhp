// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpOptionalGenericIdentifier
        ///     : Identifier=identifier
        ///     | GenericIdentifier=tyhpGenericIdentifier {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitTyhpOptionalGenericIdentifier([NotNull] TyhpParser.TyhpOptionalGenericIdentifierContext context)
        {
            return context switch
            {
                _ when context.Identifier != null => this.VisitIdentifier(context.Identifier),
                _ when context.GenericIdentifier != null => this.VisitTyhpGenericIdentifier(context.GenericIdentifier),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpOptionalGenericIdentifier",
                    () => PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }
