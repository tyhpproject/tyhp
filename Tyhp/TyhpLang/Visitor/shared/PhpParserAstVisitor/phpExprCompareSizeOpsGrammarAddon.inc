// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprCompareSizeOpsGrammarAddon([NotNull] TyhpParser.PhpExprCompareSizeOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );
