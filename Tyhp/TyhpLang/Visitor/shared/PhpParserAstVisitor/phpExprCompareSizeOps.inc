// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprCompareSizeOps([NotNull] TyhpParser.PhpExprCompareSizeOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprCompareSizeOpsGrammarAddon(context.phpExprCompareSizeOpsGrammarAddon())
            );
