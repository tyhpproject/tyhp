// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// <c>async { ... }</c> — a Promise-valued block, not a callable.
        /// </summary>
        public override IExpression VisitPhpExprPrecBaseGrammarAddon(
            [NotNull] TyhpParser.PhpExprPrecBaseGrammarAddonContext context)
        {
            var body = context.StatementList != null
                ? this.VisitInnerStatementList(context.StatementList)
                : PhpStatementBlockAst.CreateError(context, GetCurrentLanguageMode(context));
            return TyhpAsyncBlockAst.Create(body, context, GetCurrentLanguageMode(context));
        }
