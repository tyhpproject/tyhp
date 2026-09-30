// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprAssignmentOps([NotNull] TyhpParser.PhpExprAssignmentOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprAssignmentOpsGrammarAddon(context.phpExprAssignmentOpsGrammarAddon())
            );
