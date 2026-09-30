// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// functionParametersGrammarAddon
        ///     : IsExtension=T_EXTENDS? {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns a TokenValueAst for the extends keyword if present,
        /// indicating this function's first parameter is an extension receiver.
        /// Attached as the "parameters" grammar addon on PhpFunctionDeclAst.
        /// </summary>
        public override IBase2Ast? VisitFunctionParametersGrammarAddon(
            [NotNull] TyhpParser.FunctionParametersGrammarAddonContext context)
            => context.IsExtension != null
                ? this.GetTokenValueAst(context, context.IsExtension)
                : null;
