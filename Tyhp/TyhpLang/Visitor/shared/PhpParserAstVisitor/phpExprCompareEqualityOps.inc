// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprCompareEqualityOps([NotNull] TyhpParser.PhpExprCompareEqualityOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprCompareEqualityOpsGrammarAddon(context.phpExprCompareEqualityOpsGrammarAddon())
            );
