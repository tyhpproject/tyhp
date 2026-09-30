// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst VisitUseType([NotNull] TyhpParser.UseTypeContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitUseTypeGrammarAddon(context.TokenValueGrammarAddon)
            ) ?? TokenValueAst.Create("", 0, context);
