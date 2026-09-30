// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// functionNameGrammarAddon
        ///     : GenericParameters=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns the generic type arguments for the function name if present,
        /// or null if no generic parameters are specified. Attached as the
        /// "identifier" grammar addon on PhpFunctionDeclAst / PhpInlineFunctionAst.
        /// </summary>
        public override IBase2Ast? VisitFunctionNameGrammarAddon(
            [NotNull] TyhpParser.FunctionNameGrammarAddonContext context)
            => context.GenericParameters != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericParameters)
                : null;
