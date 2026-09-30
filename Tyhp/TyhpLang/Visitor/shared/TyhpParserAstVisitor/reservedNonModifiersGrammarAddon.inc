// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// reservedNonModifiersGrammarAddon
        ///     : tyhpReservedNonModifiers {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override TokenValueAst VisitReservedNonModifiersGrammarAddon([NotNull] TyhpParser.ReservedNonModifiersGrammarAddonContext context)
            => this.VisitTyhpReservedNonModifiers(context.tyhpReservedNonModifiers());
