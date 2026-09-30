// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprBinaryConcatOpsGrammarAddon([NotNull] TyhpParser.PhpExprBinaryConcatOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );
