// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// classNameIdentifierGrammarAddon
        ///     : GenericArguments=tyhpGenericTypeArguments?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitClassNameIdentifierGrammarAddon([NotNull] TyhpParser.ClassNameIdentifierGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericTypeArguments(context.GenericArguments)
                : null;
