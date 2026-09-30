// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpStatementBlockAst VisitForStatement([NotNull] TyhpParser.ForStatementContext context)
            => this.VisitInnerStatementList(context.StatementList);
