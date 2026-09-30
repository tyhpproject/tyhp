// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// functionModifiersGrammarAddon
        ///     : IsInternal=T_TYHP_INTERNAL? IsAsync=T_TYHP_ASYNC? {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns a TokenValueListAst containing the internal / async modifier tokens if present,
        /// or null if no modifiers are specified.
        /// </summary>
        public override TokenValueListAst? VisitFunctionModifiersGrammarAddon(
            [NotNull] TyhpParser.FunctionModifiersGrammarAddonContext context)
        {
            var tokens = new List<TokenValueAst>();
            if (context.IsInternal != null)
            {
                tokens.Add(this.GetTokenValueAst(context, context.IsInternal)!);
            }

            if (context.IsAsync != null)
            {
                tokens.Add(this.GetTokenValueAst(context, context.IsAsync)!);
            }

            return tokens.Count > 0
                ? TokenValueListAst.Create(tokens, context)
                : null;
        }
