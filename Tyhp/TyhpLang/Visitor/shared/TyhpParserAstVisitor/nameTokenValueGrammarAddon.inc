// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// nameTokenValueGrammarAddon
        ///     : T_TYHP_VOID {this.isLanguageMode("tyhp")}?
        ///     | T_TYHP_PARENT {this.isLanguageMode("tyhp")}?
        ///     | T_TYHP_USING {this.isLanguageMode("tyhp")}?
        ///     | T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
        ///     | T_TYHP_OPERATOR {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitNameTokenValueGrammarAddon([NotNull] TyhpParser.NameTokenValueGrammarAddonContext context)
            => PhpNameAst.Create(context.Start, context);
