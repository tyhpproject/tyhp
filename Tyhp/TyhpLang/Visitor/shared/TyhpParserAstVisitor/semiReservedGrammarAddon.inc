// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// semiReservedGrammarAddon
        ///     : tyhpSemiReserved {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override TokenValueAst VisitSemiReservedGrammarAddon([NotNull] TyhpParser.SemiReservedGrammarAddonContext context)
            => this.VisitTyhpSemiReserved(context.tyhpSemiReserved());
