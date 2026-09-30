// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitPhpExprBinaryShiftOps([NotNull] TyhpParser.PhpExprBinaryShiftOpsContext context)
            => context.TokenValue.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT && context.IsSR?.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT ?
                TokenValueAst.Create(">>", TyhpLang.Parser.TyhpParser.T_SR, context) :
                this.GetTokenValueAst(
                    context,
                    context.TokenValue,
                    () => this.VisitPhpExprBinaryShiftOpsGrammarAddon(context.phpExprBinaryShiftOpsGrammarAddon())
                );
