// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpStatementBlockAst VisitDeclareStatement([NotNull] TyhpParser.DeclareStatementContext context)
            => this.VisitInnerStatementList(context.StatementList);
