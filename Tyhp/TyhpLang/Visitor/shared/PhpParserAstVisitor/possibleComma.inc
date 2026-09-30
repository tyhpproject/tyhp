// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Parser


        public override Ast.TokenValueAst? VisitPossibleComma([NotNull] TyhpParser.PossibleCommaContext context)
            => context.T_SYM_COMMA() != null ? Ast.TokenValueAst.Create(context.T_SYM_COMMA().Symbol, context) : null;
