// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        //#endregion Identifier and Name Grammar Addons

        //#region Modifier and Parameter Grammar Addons

        /// <summary>
        /// Visits the member modifier grammar addon for Tyhp <c>async</c> / <c>internal</c>.
        ///
        /// Grammar (TyhpParser):
        ///   memberModifierGrammarAddon
        ///     : TokenValue=T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
        ///     | TokenValue=T_TYHP_INTERNAL {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override TokenValueAst VisitMemberModifierGrammarAddon([NotNull] TyhpParser.MemberModifierGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue)
                ?? TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
