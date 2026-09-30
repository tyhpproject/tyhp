// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprBinaryConcatOps([NotNull] TyhpParser.PhpExprBinaryConcatOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprBinaryConcatOpsGrammarAddon(context.phpExprBinaryConcatOpsGrammarAddon())
            );
