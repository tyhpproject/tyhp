// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Overrides the grammar addon for unary prefix operators.
        /// Tyhp adds: T_DECIMAL_CAST, T_TYHP_AWAIT
        /// </summary>
        public override TokenValueAst? VisitPhpExprUnaryPreOpsGrammarAddon([NotNull] TyhpParser.PhpExprUnaryPreOpsGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue);
