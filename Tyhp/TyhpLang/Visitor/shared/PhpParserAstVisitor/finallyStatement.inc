// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpStatementBlockAst? VisitFinallyStatement([NotNull] TyhpParser.FinallyStatementContext context)
            => context.StatementList != null ? this.VisitInnerStatementList(context.StatementList) : null;
