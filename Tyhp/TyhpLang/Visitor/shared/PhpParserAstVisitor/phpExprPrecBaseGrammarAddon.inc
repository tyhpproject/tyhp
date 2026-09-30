// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override IExpression VisitPhpExprPrecBaseGrammarAddon([NotNull] TyhpParser.PhpExprPrecBaseGrammarAddonContext context)
            => UnexpectedNodeAst.Create(context);
