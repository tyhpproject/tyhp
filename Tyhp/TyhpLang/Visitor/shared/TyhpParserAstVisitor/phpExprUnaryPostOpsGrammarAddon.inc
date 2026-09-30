// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Overrides the grammar addon for unary postfix operators.
        /// Tyhp does not currently add new postfix operators.
        /// </summary>
        public override TokenValueAst? VisitPhpExprUnaryPostOpsGrammarAddon([NotNull] TyhpParser.PhpExprUnaryPostOpsGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue);
